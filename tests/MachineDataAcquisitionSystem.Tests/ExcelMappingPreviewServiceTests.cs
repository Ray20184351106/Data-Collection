using System;
using System.Collections.Generic;
using System.IO;
using MachineDataAcquisitionSystem.Core.Mapping;
using NPOI.HSSF.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class ExcelMappingPreviewServiceTests
    {
        [Fact]
        public void Preview_resolves_all_supported_locators_and_applies_whitelisted_conversions()
        {
            string workbookPath = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Serial number");
                sheet.GetRow(0).CreateCell(1).SetCellValue("  SN-001  ");

                sheet.CreateRow(1).CreateCell(3).SetCellValue("12.34");

                sheet.CreateRow(2).CreateCell(0).SetCellValue("Panel count");
                sheet.GetRow(2).CreateCell(2).SetCellValue("42");

                sheet.CreateRow(4).CreateCell(1).SetCellValue("Measured at");
                sheet.CreateRow(5).CreateCell(1).SetCellValue(new DateTime(2026, 8, 6, 9, 30, 0));
            });

            try
            {
                var rule = CreateRule(
                    Field("SerialNumber", "string", true,
                        new MappingLocator { Type = "labelOffset", Text = "Serial number", ColumnOffset = 1 },
                        "trim"),
                    Field("Measurement", "decimal", true,
                        Cell("D2", "A1", "Serial number"),
                        "decimal"),
                    Field("PanelCount", "int", true,
                        new MappingLocator { Type = "rowKey", Text = "Panel count", ValueColumn = "C" },
                        "integer"),
                    Field("MeasuredAt", "DateTime", true,
                        new MappingLocator { Type = "headerColumn", Text = "Measured at", DataRowOffset = 1 },
                        "date"));

                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(workbookPath, rule);

                Assert.True(result.IsValid, string.Join(Environment.NewLine, result.ErrorCodes));
                Assert.Equal("SN-001", result.Fields["SerialNumber"].Value);
                Assert.Equal(12.34m, (decimal)result.Fields["Measurement"].Value);
                Assert.Equal(42, (int)result.Fields["PanelCount"].Value);
                Assert.Equal(new DateTime(2026, 8, 6, 9, 30, 0), (DateTime)result.Fields["MeasuredAt"].Value);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        [Fact]
        public void Preview_reports_missing_required_value_without_running_the_acquisition_pipeline()
        {
            string workbookPath = CreateWorkbook(sheet =>
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Unrelated label"));

            try
            {
                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    workbookPath,
                    CreateRule(Field(
                        "SerialNumber",
                        "string",
                        true,
                        new MappingLocator { Type = "labelOffset", Text = "Serial number", ColumnOffset = 1 })));

                Assert.False(result.IsValid);
                Assert.Contains("MISSING_REQUIRED", result.ErrorCodes);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        [Fact]
        public void Preview_rejects_an_ambiguous_label_instead_of_choosing_the_first_match()
        {
            string workbookPath = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Serial number");
                sheet.GetRow(0).CreateCell(1).SetCellValue("SN-001");
                sheet.CreateRow(2).CreateCell(0).SetCellValue("Serial number");
                sheet.GetRow(2).CreateCell(1).SetCellValue("SN-002");
            });

            try
            {
                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    workbookPath,
                    CreateRule(Field(
                        "SerialNumber",
                        "string",
                        true,
                        new MappingLocator { Type = "labelOffset", Text = "Serial number", ColumnOffset = 1 })));

                Assert.False(result.IsValid);
                Assert.Contains("AMBIGUOUS_LABEL", result.ErrorCodes);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        [Fact]
        public void Preview_rejects_a_cell_locator_outside_the_xlsx_grid()
        {
            string workbookPath = CreateWorkbook(sheet =>
                sheet.CreateRow(0).CreateCell(0).SetCellValue("sample"));

            try
            {
                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    workbookPath,
                    CreateRule(Field(
                        "SerialNumber",
                        "string",
                        true,
                        Cell("XFE1", "A1", "sample"))));

                Assert.False(result.IsValid);
                Assert.Contains("LOCATOR_OUT_OF_RANGE", result.ErrorCodes);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        [Fact]
        public void Preview_rejects_a_value_that_cannot_be_assigned_to_the_target_type()
        {
            string workbookPath = CreateWorkbook(sheet =>
                sheet.CreateRow(0).CreateCell(0).SetCellValue("ABC"));
            try
            {
                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    workbookPath,
                    CreateRule(Field("PanelCount", "int", true,
                        Cell("A1", "A1", "ABC"))));

                Assert.False(result.IsValid);
                Assert.Contains("CONVERSION_FAILED", result.ErrorCodes);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        [Fact]
        public void Preview_applies_a_default_to_a_missing_optional_cell_before_type_conversion()
        {
            string workbookPath = CreateWorkbook(sheet =>
                sheet.CreateRow(0).CreateCell(0).SetCellValue("sample"));
            try
            {
                FieldMappingRule field = Field("PanelCount", "int", false,
                    Cell("B2", "A1", "sample"),
                    "integer", "default");
                field.DefaultValue = "7";

                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    workbookPath,
                    CreateRule(field));

                Assert.True(result.IsValid, string.Join(Environment.NewLine, result.ErrorCodes));
                Assert.Equal(7, result.Fields["PanelCount"].Value);
                Assert.Contains("OPTIONAL_VALUE_MISSING", result.WarningCodes);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        [Fact]
        public void Preview_applies_an_exact_value_map()
        {
            string workbookPath = CreateWorkbook(sheet =>
                sheet.CreateRow(0).CreateCell(0).SetCellValue("OK"));
            try
            {
                FieldMappingRule field = Field("Disposition", "string", true,
                    Cell("A1", "A1", "OK"),
                    "valueMap");
                field.ExactValueMap = new Dictionary<string, string> { { "OK", "ACC" } };

                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    workbookPath,
                    CreateRule(field));

                Assert.True(result.IsValid, string.Join(Environment.NewLine, result.ErrorCodes));
                Assert.Equal("ACC", result.Fields["Disposition"].Value);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        [Fact]
        public void Preview_rejects_fractional_numbers_for_integer_targets()
        {
            string workbookPath = CreateWorkbook(sheet =>
                sheet.CreateRow(0).CreateCell(0).SetCellValue(1.9d));
            try
            {
                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    workbookPath,
                    CreateRule(Field("PanelCount", "int", true,
                        Cell("A1", "A1", "1.9"),
                        "integer")));

                Assert.False(result.IsValid);
                Assert.Contains("CONVERSION_FAILED", result.ErrorCodes);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        [Fact]
        public void Preview_reads_a_dynamically_generated_xls_workbook()
        {
            string workbookPath = Path.Combine(
                Path.GetTempPath(),
                "MappingPreview_" + Guid.NewGuid().ToString("N") + ".xls");
            using (var workbook = new HSSFWorkbook())
            using (var stream = File.Create(workbookPath))
            {
                workbook.CreateSheet("Data").CreateRow(0).CreateCell(0).SetCellValue("SN-XLS");
                workbook.Write(stream);
            }

            try
            {
                MappingRuleDefinition rule = CreateRule(Field("SerialNumber", "string", true,
                    Cell("A1", "A1", "SN-XLS")));
                rule.NormalizedExtension = ".xls";

                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(workbookPath, rule);

                Assert.True(result.IsValid, string.Join(Environment.NewLine, result.ErrorCodes));
                Assert.Equal("SN-XLS", result.Fields["SerialNumber"].Value);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        private static MappingRuleDefinition CreateRule(params FieldMappingRule[] fields)
        {
            return new MappingRuleDefinition
            {
                RuleName = "inspection-rule",
                ModelId = 7,
                TargetModelType = "InspectionRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = ".xlsx",
                SheetName = "Data",
                Fields = new List<FieldMappingRule>(fields)
            };
        }

        [Fact]
        public void Preview_rejects_a_fixed_cell_when_its_structural_anchor_changed()
        {
            string matchingPath = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Template A");
                sheet.CreateRow(1).CreateCell(1).SetCellValue("10");
            });
            string changedPath = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Template B");
                sheet.CreateRow(1).CreateCell(1).SetCellValue("20");
            });
            try
            {
                MappingRuleDefinition rule = CreateRule(Field(
                    "PanelCount",
                    "int",
                    true,
                    Cell("B2", "A1", "Template A"),
                    "integer"));
                MappingPreviewResult validated = new ExcelMappingPreviewService().Preview(matchingPath, rule);
                Assert.True(validated.IsValid, string.Join(Environment.NewLine, validated.ErrorCodes));
                rule.TemplateSignature = validated.TemplateSignature;

                MappingPreviewResult changed = new ExcelMappingPreviewService().Preview(changedPath, rule);

                Assert.False(changed.IsValid);
                Assert.Contains("CELL_ANCHOR_MISMATCH", changed.ErrorCodes);
                Assert.Contains("TEMPLATE_SIGNATURE_MISMATCH", changed.ErrorCodes);
            }
            finally
            {
                File.Delete(changedPath);
                File.Delete(matchingPath);
            }
        }

        [Fact]
        public void Published_signature_allows_an_optional_label_to_be_absent_and_use_its_default()
        {
            string matchingPath = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Optional note");
                sheet.GetRow(0).CreateCell(1).SetCellValue("present");
            });
            string missingPath = CreateWorkbook(sheet =>
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Unrelated"));
            try
            {
                FieldMappingRule optional = Field(
                    "Note",
                    "string",
                    false,
                    new MappingLocator
                    {
                        Type = "labelOffset",
                        Text = "Optional note",
                        ColumnOffset = 1
                    },
                    "default");
                optional.DefaultValue = "fallback";
                MappingRuleDefinition rule = CreateRule(optional);

                MappingPreviewResult validated = new ExcelMappingPreviewService().Preview(matchingPath, rule);
                Assert.True(validated.IsValid, string.Join(Environment.NewLine, validated.ErrorCodes));
                rule.TemplateSignature = validated.TemplateSignature;

                MappingPreviewResult missing = new ExcelMappingPreviewService().Preview(missingPath, rule);

                Assert.True(missing.IsValid, string.Join(Environment.NewLine, missing.ErrorCodes));
                Assert.Equal("fallback", missing.Fields["Note"].Value);
                Assert.Contains("OPTIONAL_VALUE_MISSING", missing.WarningCodes);
                Assert.Equal(validated.TemplateSignature, missing.TemplateSignature);
            }
            finally
            {
                File.Delete(missingPath);
                File.Delete(matchingPath);
            }
        }

        [Fact]
        public void Preview_rejects_a_default_value_on_a_required_field()
        {
            string workbookPath = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Lot number");
                sheet.GetRow(0).CreateCell(1).SetCellValue(string.Empty);
            });
            try
            {
                FieldMappingRule required = Field(
                    "LotNumber",
                    "string",
                    true,
                    new MappingLocator
                    {
                        Type = "labelOffset",
                        Text = "Lot number",
                        ColumnOffset = 1
                    },
                    "default");
                required.DefaultValue = "UNKNOWN";

                MappingPreviewResult result = new ExcelMappingPreviewService().Preview(
                    workbookPath,
                    CreateRule(required));

                Assert.False(result.IsValid);
                Assert.Contains("INVALID_RULE", result.ErrorCodes);
            }
            finally
            {
                File.Delete(workbookPath);
            }
        }

        private static MappingLocator Cell(string cell, string anchorCell, string anchorText)
        {
            return new MappingLocator
            {
                Type = "cell",
                Cell = cell,
                AnchorCell = anchorCell,
                AnchorText = anchorText
            };
        }

        private static FieldMappingRule Field(
            string targetField,
            string targetType,
            bool isRequired,
            MappingLocator locator,
            params string[] transforms)
        {
            return new FieldMappingRule
            {
                TargetField = targetField,
                TargetType = targetType,
                IsRequired = isRequired,
                Locator = locator,
                Transforms = new List<string>(transforms)
            };
        }

        private static string CreateWorkbook(Action<XSSFSheet> configure)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "MappingPreview_" + Guid.NewGuid().ToString("N") + ".xlsx");

            using (var workbook = new XSSFWorkbook())
            using (var stream = File.Create(path))
            {
                var sheet = (XSSFSheet)workbook.CreateSheet("Data");
                configure(sheet);
                workbook.Write(stream);
            }

            return path;
        }
    }
}
