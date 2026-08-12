using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MachineDataAcquisitionSystem.Core.Mapping;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class RepeatingRowMappingTests
    {
        [Fact]
        public void Deserialize_old_rule_defaults_to_single_record_and_common_fields()
        {
            const string json = "{\"RuleName\":\"legacy\",\"ModelId\":7,\"TargetModelType\":\"InspectionRecord\",\"ModelSchemaHash\":\"schema-v1\",\"NormalizedExtension\":\".xlsx\",\"SheetName\":\"Data\",\"Fields\":[{\"TargetField\":\"Value\",\"TargetType\":\"string\",\"Locator\":{\"Type\":\"labelOffset\",\"Text\":\"Value\",\"ColumnOffset\":1}}]}";

            MappingRuleDefinition rule = MappingRuleSerializer.Deserialize(json);

            Assert.Equal(MappingRecordMode.SingleRecord, rule.RecordMode);
            Assert.Null(rule.RepeatedRows);
            Assert.Equal(MappingFieldScope.Common, rule.Fields[0].Scope);
            MappingRuleSerializer.ValidateDefinition(rule);
        }

        [Fact]
        public void Preview_reads_repeating_rows_copies_common_fields_and_normalizes_unicode_minus()
        {
            string path = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Lot");
                sheet.GetRow(0).CreateCell(1).SetCellValue("LOT-001");
                sheet.CreateRow(3).CreateCell(3).SetCellValue("标准值");
                sheet.GetRow(3).CreateCell(4).SetCellValue("下公差");
                sheet.GetRow(3).CreateCell(5).SetCellValue("测量值");
                sheet.CreateRow(4).CreateCell(1).SetCellValue("D1 (L1,L3)");
                sheet.GetRow(4).CreateCell(3).SetCellValue("314.4");
                sheet.GetRow(4).CreateCell(4).SetCellValue("−0.08");
                sheet.GetRow(4).CreateCell(5).SetCellValue("314.395");
                sheet.CreateRow(5).CreateCell(1).SetCellValue("D2 (L2,L4)");
                sheet.GetRow(5).CreateCell(3).SetCellValue("175");
                sheet.GetRow(5).CreateCell(4).SetCellValue("-0.08");
                sheet.GetRow(5).CreateCell(5).SetCellValue("175.006");
                sheet.CreateRow(6).CreateCell(1).SetCellValue(string.Empty);
                sheet.CreateRow(7).CreateCell(1).SetCellValue("SHOULD-NOT-BE-READ");
            });

            try
            {
                MappingPreviewResult preview = new ExcelMappingPreviewService().Preview(path, CreateRepeatingRule());

                Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.ErrorCodes));
                Assert.Equal(2, preview.Records.Count);
                Assert.Equal(5, preview.Records[0].ExcelRowNumber);
                Assert.Equal("LOT-001", preview.Records[0].Fields["LotNumber"].Value);
                Assert.Equal("D1 (L1,L3)", preview.Records[0].Fields["Point"].Value);
                Assert.Equal(314.4m, preview.Records[0].Fields["StandardValue"].Value);
                Assert.Equal(-0.08m, preview.Records[0].Fields["LowerTolerance"].Value);
                Assert.Equal(175.006m, preview.Records[1].Fields["MeasuredValue"].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_reports_the_excel_row_and_field_when_any_repeating_row_is_invalid()
        {
            string path = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Lot");
                sheet.GetRow(0).CreateCell(1).SetCellValue("LOT-001");
                sheet.CreateRow(3).CreateCell(3).SetCellValue("标准值");
                sheet.GetRow(3).CreateCell(4).SetCellValue("下公差");
                sheet.GetRow(3).CreateCell(5).SetCellValue("测量值");
                sheet.CreateRow(4).CreateCell(1).SetCellValue("D1");
                sheet.GetRow(4).CreateCell(3).SetCellValue("314.4");
                sheet.GetRow(4).CreateCell(4).SetCellValue("-0.08");
                sheet.GetRow(4).CreateCell(5).SetCellValue("314.395");
                sheet.CreateRow(5).CreateCell(1).SetCellValue("D2");
                sheet.GetRow(5).CreateCell(3).SetCellValue("not-a-number");
                sheet.GetRow(5).CreateCell(4).SetCellValue("-0.08");
                sheet.GetRow(5).CreateCell(5).SetCellValue("175.006");
            });

            try
            {
                MappingPreviewResult preview = new ExcelMappingPreviewService().Preview(path, CreateRepeatingRule());

                Assert.False(preview.IsValid);
                Assert.Contains("CONVERSION_FAILED", preview.ErrorCodes);
                Assert.Equal(6, preview.Records[1].ExcelRowNumber);
                Assert.Equal("CONVERSION_FAILED", preview.Records[1].Fields["StandardValue"].ErrorCode);
                Assert.Equal("D6", preview.Records[1].Fields["StandardValue"].SourceCell);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Runtime_rejects_the_whole_file_with_excel_row_cell_and_field_details()
        {
            string path = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Lot");
                sheet.GetRow(0).CreateCell(1).SetCellValue("LOT-001");
                sheet.CreateRow(3).CreateCell(3).SetCellValue("标准值");
                sheet.GetRow(3).CreateCell(4).SetCellValue("下公差");
                sheet.GetRow(3).CreateCell(5).SetCellValue("测量值");
                sheet.CreateRow(4).CreateCell(1).SetCellValue("D1");
                sheet.GetRow(4).CreateCell(3).SetCellValue("314.4");
                sheet.GetRow(4).CreateCell(4).SetCellValue("-0.08");
                sheet.GetRow(4).CreateCell(5).SetCellValue("314.395");
                sheet.CreateRow(5).CreateCell(1).SetCellValue("D2");
                sheet.GetRow(5).CreateCell(3).SetCellValue("bad-number");
                sheet.GetRow(5).CreateCell(4).SetCellValue("-0.08");
                sheet.GetRow(5).CreateCell(5).SetCellValue("175.006");
            });

            try
            {
                string json = MappingRuleSerializer.Serialize(CreateRepeatingRule());
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                MappingValidationException error = Assert.Throws<MappingValidationException>(() =>
                    MappingRuntime.CreateModels<InspectionRecord>(encoded, path, 1));

                Assert.Contains("Excel第6行", error.Message);
                Assert.Contains("D6", error.Message);
                Assert.Contains("StandardValue", error.Message);
                Assert.Contains("CONVERSION_FAILED", error.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Preview_supports_xls_and_xlsx_when_the_table_moves(bool useLegacyXls)
        {
            string path = CreateWorkbook(useLegacyXls, sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("Lot");
                sheet.GetRow(0).CreateCell(1).SetCellValue("LOT-MOVED");
                sheet.CreateRow(10).CreateCell(7).SetCellValue("标准值");
                sheet.GetRow(10).CreateCell(8).SetCellValue("下公差");
                sheet.GetRow(10).CreateCell(9).SetCellValue("测量值");
                sheet.CreateRow(11).CreateCell(5).SetCellValue("D8 (L2,L3)");
                sheet.GetRow(11).CreateCell(7).SetCellValue("3");
                sheet.GetRow(11).CreateCell(8).SetCellValue("−0.08");
                sheet.GetRow(11).CreateCell(9).SetCellValue("3.002");
            });

            try
            {
                MappingRuleDefinition rule = CreateRepeatingRule();
                rule.NormalizedExtension = useLegacyXls ? ".xls" : ".xlsx";
                MappingPreviewResult preview = new ExcelMappingPreviewService().Preview(path, rule);

                Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.ErrorCodes));
                Assert.Single(preview.Records);
                Assert.Equal(12, preview.Records[0].ExcelRowNumber);
                Assert.Equal("D8 (L2,L3)", preview.Records[0].Fields["Point"].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_rejects_a_non_unique_header_anchor()
        {
            string path = CreateWorkbook(sheet =>
            {
                sheet.CreateRow(0).CreateCell(0).SetCellValue("标准值");
                sheet.CreateRow(3).CreateCell(3).SetCellValue("标准值");
            });

            try
            {
                MappingPreviewResult preview = new ExcelMappingPreviewService().Preview(path, CreateRepeatingRule());

                Assert.False(preview.IsValid);
                Assert.Contains("AMBIGUOUS_TABLE_ANCHOR", preview.ErrorCodes);
                Assert.Empty(preview.Records);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Repeating_rule_requires_a_mapped_key_column()
        {
            MappingRuleDefinition rule = CreateRepeatingRule();
            rule.RepeatedRows.KeyColumnOffset = -1;

            MappingValidationException error = Assert.Throws<MappingValidationException>(() =>
                MappingRuleSerializer.ValidateDefinition(rule));

            Assert.Contains("关键列", error.Message);
        }

        [Fact]
        public void Repeating_rule_rejects_row_fields_outside_the_selected_column_range()
        {
            MappingRuleDefinition rule = CreateRepeatingRule();
            rule.Fields.Find(field => field.TargetField == "MeasuredValue").Locator.ColumnOffset = 3;

            MappingValidationException error = Assert.Throws<MappingValidationException>(() =>
                MappingRuleSerializer.ValidateDefinition(rule));

            Assert.Contains("列范围", error.Message);
        }

        [Fact]
        public void Generator_returns_a_model_collection_only_for_repeating_rules()
        {
            string script = new MappingScriptGenerator().Generate(CreateRepeatingRule());

            Assert.Contains("MappingRuntime.CreateModels<InspectionRecord>", script);
            Assert.Contains("return models;", script);
            Assert.DoesNotContain("PopulateModel(model", script);
        }

        [Fact]
        public void Result_normalizer_accepts_single_or_collection_and_rejects_mixed_types()
        {
            var first = new InspectionRecord();
            var second = new InspectionRecord();

            Assert.Single(MappingResultNormalizer.Normalize(first, "InspectionRecord"));
            Assert.Equal(2, MappingResultNormalizer.Normalize(
                new List<InspectionRecord> { first, second }, "InspectionRecord").Count);
            Assert.Throws<MappingValidationException>(() => MappingResultNormalizer.Normalize(
                new object[] { first, new object() }, "InspectionRecord"));
            Assert.Throws<MappingValidationException>(() => MappingResultNormalizer.Normalize(
                Enumerable.Repeat(first, MappingResultNormalizer.MaximumRecordsPerFile + 1).ToList(),
                "InspectionRecord"));
        }

        private static MappingRuleDefinition CreateRepeatingRule()
        {
            return new MappingRuleDefinition
            {
                RuleName = "measurement-rows",
                ModelId = 7,
                TargetModelType = "InspectionRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = ".xlsx",
                SheetName = "Data",
                RecordMode = MappingRecordMode.RepeatingRows,
                RepeatedRows = new RepeatedRowDefinition
                {
                    AnchorMode = MappingTableAnchorMode.HeaderText,
                    AnchorText = "标准值",
                    AnchorCell = "D4",
                    FirstDataRowOffset = 1,
                    KeyColumnOffset = -2,
                    FirstColumnOffset = -2,
                    LastColumnOffset = 2,
                    StopOnBlankKey = true
                },
                Fields = new List<FieldMappingRule>
                {
                    Common("LotNumber", "string", new MappingLocator
                    {
                        Type = "labelOffset", Text = "Lot", ColumnOffset = 1
                    }),
                    Row("Point", "string", true, -2, string.Empty),
                    Row("StandardValue", "decimal", true, 0, "标准值", "decimal"),
                    Row("LowerTolerance", "decimal", true, 1, "下公差", "decimal"),
                    Row("MeasuredValue", "decimal", true, 2, "测量值", "decimal")
                }
            };
        }

        private static FieldMappingRule Common(string name, string type, MappingLocator locator)
        {
            return new FieldMappingRule
            {
                TargetField = name,
                TargetType = type,
                Scope = MappingFieldScope.Common,
                IsRequired = true,
                Locator = locator,
                Transforms = new List<string> { "trim" }
            };
        }

        private static FieldMappingRule Row(
            string name,
            string type,
            bool required,
            int columnOffset,
            string expectedHeader,
            params string[] transforms)
        {
            return new FieldMappingRule
            {
                TargetField = name,
                TargetType = type,
                Scope = MappingFieldScope.RowColumn,
                IsRequired = required,
                Locator = new MappingLocator
                {
                    Type = "rowColumn",
                    Text = expectedHeader,
                    ColumnOffset = columnOffset
                },
                Transforms = new List<string>(transforms)
            };
        }

        private static string CreateWorkbook(Action<XSSFSheet> configure)
        {
            return CreateWorkbook(false, sheet => configure((XSSFSheet)sheet));
        }

        private static string CreateWorkbook(bool useLegacyXls, Action<ISheet> configure)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "RepeatingRows_" + Guid.NewGuid().ToString("N") + (useLegacyXls ? ".xls" : ".xlsx"));
            using (IWorkbook workbook = useLegacyXls ? (IWorkbook)new HSSFWorkbook() : new XSSFWorkbook())
            using (var stream = File.Create(path))
            {
                ISheet sheet = workbook.CreateSheet("Data");
                configure(sheet);
                workbook.Write(stream);
            }
            return path;
        }

        private sealed class InspectionRecord
        {
            public InspectionRecord() { }
            public string LotNumber { get; set; }
            public string Point { get; set; }
            public decimal StandardValue { get; set; }
            public decimal LowerTolerance { get; set; }
            public decimal MeasuredValue { get; set; }
        }
    }
}
