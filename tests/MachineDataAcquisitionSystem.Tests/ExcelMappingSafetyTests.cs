using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MachineDataAcquisitionSystem.Core.Mapping;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class ExcelMappingSafetyTests
    {
        [Fact]
        public void Preview_rejects_an_xlsx_zip_renamed_to_xls()
        {
            string xlsxPath = CreateWorkbook(".xlsx", workbook =>
                workbook.GetSheet("Data").CreateRow(0).CreateCell(0).SetCellValue("SN-001"));
            string disguisedPath = Path.ChangeExtension(xlsxPath, ".xls");
            File.Copy(xlsxPath, disguisedPath, false);

            try
            {
                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    disguisedPath,
                    NewCellRule(".xls", "A1", "SN-001"));

                Assert.False(result.IsValid);
                Assert.Contains("FILE_SIGNATURE_MISMATCH", result.ErrorCodes);
            }
            finally
            {
                File.Delete(disguisedPath);
                File.Delete(xlsxPath);
            }
        }

        [Fact]
        public void Preview_never_changes_the_original_workbook_bytes()
        {
            string path = CreateWorkbook(".xlsx", workbook =>
                workbook.GetSheet("Data").CreateRow(0).CreateCell(0).SetCellValue("SN-001"));

            try
            {
                string before = MappingRuleSerializer.FileSha256(path);

                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    path,
                    NewCellRule(".xlsx", "A1", "SN-001"));

                string after = MappingRuleSerializer.FileSha256(path);
                Assert.True(result.IsValid, string.Join(Environment.NewLine, result.ErrorCodes));
                Assert.Equal(before, after);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_reads_a_workbook_while_an_editor_holds_it_open_for_writing()
        {
            string path = CreateWorkbook(".xlsx", workbook =>
                workbook.GetSheet("Data").CreateRow(0).CreateCell(0).SetCellValue("SN-001"));

            try
            {
                using (FileStream editorHandle = File.Open(
                    path,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                        path,
                        NewCellRule(".xlsx", "A1", "SN-001"));

                    Assert.True(result.IsValid, string.Join(Environment.NewLine, result.ErrorCodes));
                    Assert.Equal("SN-001", result.Fields["Value"].Value);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_reads_a_formula_cached_value_without_recalculating_the_formula()
        {
            EnsureFormulaDependencyAvailable();
            string path = CreateWorkbook(".xlsx", workbook =>
            {
                ISheet sheet = workbook.GetSheet("Data");
                ICell source = sheet.CreateRow(0).CreateCell(0);
                source.SetCellValue(10d);
                ICell formula = sheet.GetRow(0).CreateCell(1);
                formula.SetCellFormula("A1*2");
                workbook.GetCreationHelper().CreateFormulaEvaluator().EvaluateFormulaCell(formula);
                source.SetCellValue(99d);
            });
            AssertWorkbookCanBeOpened(path);

            try
            {
                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    path,
                    NewCellRule(".xlsx", "B1", "99", "decimal"));

                Assert.True(result.IsValid, string.Join(Environment.NewLine, result.ErrorCodes));
                Assert.Equal(20m, (decimal)result.Fields["Value"].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_rejects_a_formula_without_a_cached_result_instead_of_evaluating_it()
        {
            EnsureFormulaDependencyAvailable();
            string path = CreateWorkbook(".xlsx", workbook =>
            {
                ISheet sheet = workbook.GetSheet("Data");
                sheet.CreateRow(0).CreateCell(0).SetCellValue(10d);
                sheet.GetRow(0).CreateCell(1).SetCellFormula("A1*2");
            });
            AssertWorkbookCanBeOpened(path);

            try
            {
                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    path,
                    NewCellRule(".xlsx", "B1", "10", "decimal"));

                Assert.False(result.IsValid);
                Assert.Contains("FORMULA_CACHE_MISSING", result.ErrorCodes);
                Assert.Null(result.Fields["Value"].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static MappingRuleDefinition NewCellRule(
            string extension,
            string cell,
            string anchorText,
            params string[] transforms)
        {
            return new MappingRuleDefinition
            {
                RuleName = "safety-rule",
                ModelId = 7,
                TargetModelType = "InspectionRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = extension,
                SheetName = "Data",
                Fields = new List<FieldMappingRule>
                {
                    new FieldMappingRule
                    {
                        TargetField = "Value",
                        TargetType = transforms.Length == 0 ? "string" : "decimal",
                        IsRequired = true,
                        Locator = new MappingLocator
                        {
                            Type = "cell",
                            Cell = cell,
                            AnchorCell = "A1",
                            AnchorText = anchorText
                        },
                        Transforms = new List<string>(transforms)
                    }
                }
            };
        }

        [Fact]
        public void Readonly_copy_rechecks_the_copied_file_size_before_workbook_parsing()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "MappingSafety_Oversized_" + Guid.NewGuid().ToString("N") + ".xlsx");
            using (FileStream stream = File.Create(path))
                stream.SetLength(ExcelMappingPreviewService.MaximumFileBytes + 1);

            try
            {
                MethodInfo method = typeof(ExcelMappingPreviewService).GetMethod(
                    "OpenReadonlyCopy",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(method);

                TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
                    method.Invoke(null, new object[] { path }));

                MappingValidationException validation = Assert.IsType<MappingValidationException>(
                    exception.InnerException);
                Assert.Equal("FILE_SIZE_LIMIT", validation.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string CreateWorkbook(string extension, Action<XSSFWorkbook> configure)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "MappingSafety_" + Guid.NewGuid().ToString("N") + extension);

            using (var workbook = new XSSFWorkbook())
            using (var stream = File.Create(path))
            {
                workbook.CreateSheet("Data");
                configure(workbook);
                workbook.Write(stream);
            }

            return path;
        }

        private static void EnsureFormulaDependencyAvailable()
        {
            string testOutput = AppDomain.CurrentDomain.BaseDirectory;
            string configuration = new DirectoryInfo(testOutput).Parent.Name;
            string repositoryRoot = Path.GetFullPath(Path.Combine(
                testOutput,
                "..", "..", "..", "..", ".."));
            string assemblyPath = Path.Combine(
                repositoryRoot,
                "MachineDataAcquisitionSystem",
                "bin",
                configuration,
                "SkiaSharp.dll");

            if (File.Exists(assemblyPath))
            {
                string localAssemblyPath = Path.Combine(testOutput, "SkiaSharp.dll");
                if (!File.Exists(localAssemblyPath))
                    File.Copy(assemblyPath, localAssemblyPath, false);
                Assembly.LoadFrom(localAssemblyPath);
            }
        }

        private static void AssertWorkbookCanBeOpened(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (IWorkbook workbook = WorkbookFactory.Create(stream))
                Assert.NotNull(workbook.GetSheet("Data"));
        }
    }
}
