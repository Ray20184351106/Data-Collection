using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Core.Mapping;
using MachineDataAcquisitionSystem.Models;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class MasterDetailMappingTests
    {
        [Fact]
        public void Filename_parser_extracts_ascii_and_chinese_brackets_from_left_to_right()
        {
            FileNameExtractionResult result = FileNameExtractionParser.Parse(
                @"C:\incoming\设备A[批次01]前缀【工单02】.xlsx",
                new FileNameExtractionDefinition { ExpectedSegmentCount = 2 });

            Assert.Equal("设备A[批次01]前缀【工单02】.xlsx", result.FullName);
            Assert.Equal("设备A[批次01]前缀【工单02】", result.Stem);
            Assert.Equal(new[] { "批次01", "工单02" }, result.Segments);
        }

        [Theory]
        [InlineData("设备[A].xlsx", 2)]
        [InlineData("设备[A][].xlsx", 2)]
        [InlineData("设备[A【B】].xlsx", 1)]
        [InlineData("设备[A.xlsx", 1)]
        [InlineData("设备A].xlsx", 1)]
        public void Filename_parser_rejects_count_empty_nested_and_unbalanced_segments(
            string fileName,
            int expectedCount)
        {
            Assert.Throws<MappingValidationException>(() =>
                FileNameExtractionParser.Parse(
                    fileName,
                    new FileNameExtractionDefinition { ExpectedSegmentCount = expectedCount }));
        }

        [Fact]
        public void Deserialize_old_rule_keeps_single_table_contract()
        {
            const string json = "{\"RuleName\":\"legacy\",\"ModelId\":7,\"TargetModelType\":\"InspectionRecord\",\"ModelSchemaHash\":\"schema-v1\",\"NormalizedExtension\":\".xlsx\",\"SheetName\":\"Data\",\"Fields\":[{\"TargetField\":\"Value\",\"TargetType\":\"string\",\"IsRequired\":true,\"Locator\":{\"Type\":\"labelOffset\",\"Text\":\"Value\",\"ColumnOffset\":1}}]}";

            MappingRuleDefinition result = MappingRuleSerializer.Deserialize(json);

            Assert.Equal(MappingRecordMode.SingleRecord, result.RecordMode);
            Assert.Null(result.MasterDetail);
            MappingRuleSerializer.ValidateDefinition(result);
        }

        [Fact]
        public void Master_detail_rule_requires_filename_master_fields_repeating_details_and_long_parent_link()
        {
            MappingRuleDefinition rule = CreateRule();

            MappingRuleSerializer.ValidateDefinition(rule);

            rule.MasterDetail.ParentCidField = "CID";
            Assert.Throws<MappingValidationException>(() => MappingRuleSerializer.ValidateDefinition(rule));
        }

        [Fact]
        public void Generator_returns_a_typed_master_detail_result_and_contract_checks_both_models()
        {
            string script = new MappingScriptGenerator().Generate(CreateRule());

            Assert.Contains("new FileHeaderModel()", script);
            Assert.Contains("new InspectionDetailModel()", script);
            Assert.Contains("MappingRuntime.CreateMasterDetail<FileHeaderModel, InspectionDetailModel>", script);
            Assert.Contains("return result;", script);
        }

        [Fact]
        public void Result_normalizer_rejects_empty_details_and_wrong_model_types()
        {
            var valid = new MasterDetailParseResult(
                new FileHeaderModel(),
                new object[] { new InspectionDetailModel() },
                11,
                "FileHeaderModel",
                12,
                "InspectionDetailModel",
                "PARENT_CID");

            MasterDetailParseResult normalized = MappingResultNormalizer.NormalizeMasterDetail(
                valid,
                "FileHeaderModel",
                "InspectionDetailModel");

            Assert.Same(valid, normalized);
            Assert.Throws<MappingValidationException>(() => MappingResultNormalizer.NormalizeMasterDetail(
                new MasterDetailParseResult(
                    new FileHeaderModel(),
                    Array.Empty<object>(),
                    11,
                    "FileHeaderModel",
                    12,
                    "InspectionDetailModel",
                    "PARENT_CID"),
                "FileHeaderModel",
                "InspectionDetailModel"));
            Assert.Throws<MappingValidationException>(() => MappingResultNormalizer.NormalizeMasterDetail(
                new MasterDetailParseResult(
                    new object(),
                    new object[] { new InspectionDetailModel() },
                    11,
                    "FileHeaderModel",
                    12,
                    "InspectionDetailModel",
                    "PARENT_CID"),
                "FileHeaderModel",
                "InspectionDetailModel"));
        }

        [Fact]
        public void Preview_and_runtime_create_one_filename_master_and_repeating_detail_rows()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "MasterDetailMapping_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "设备A[批次01]【工单02】.xlsx");
            try
            {
                using (var workbook = new XSSFWorkbook())
                using (var stream = File.Create(path))
                {
                    var sheet = workbook.CreateSheet("Data");
                    sheet.CreateRow(0).CreateCell(0).SetCellValue("Point");
                    sheet.CreateRow(1).CreateCell(0).SetCellValue("P1");
                    sheet.CreateRow(2).CreateCell(0).SetCellValue("P2");
                    sheet.CreateRow(3).CreateCell(0).SetCellValue(string.Empty);
                    workbook.Write(stream);
                }

                MappingRuleDefinition rule = CreateRule();
                MappingPreviewResult preview = new ExcelMappingPreviewService().Preview(path, rule);

                Assert.True(preview.IsValid, string.Join(",", preview.ErrorCodes));
                Assert.Equal(Path.GetFileName(path), preview.Fields["FileName"].Value);
                Assert.Equal(2, preview.Records.Count);

                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    MappingRuleSerializer.Serialize(rule)));
                MasterDetailParseResult result = MappingRuntime
                    .CreateMasterDetail<FileHeaderModel, InspectionDetailModel>(encoded, path, 1);

                Assert.Equal(Path.GetFileName(path), ((FileHeaderModel)result.Master).FileName);
                Assert.Equal(new[] { "P1", "P2" }, result.Details
                    .Cast<InspectionDetailModel>()
                    .Select(item => item.Point));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }

        [Fact]
        public void Versioned_script_compiles_and_executes_with_two_immutable_model_snapshots()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "MasterDetailScript_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "设备A[批次01]【工单02】.xlsx");
            try
            {
                using (var workbook = new XSSFWorkbook())
                using (var stream = File.Create(path))
                {
                    var sheet = workbook.CreateSheet("Data");
                    sheet.CreateRow(0).CreateCell(0).SetCellValue("Point");
                    sheet.CreateRow(1).CreateCell(0).SetCellValue("P1");
                    sheet.CreateRow(2).CreateCell(0).SetCellValue(string.Empty);
                    workbook.Write(stream);
                }

                MappingRuleDefinition rule = CreateRule();
                string definitionJson = MappingRuleSerializer.Serialize(rule);
                string masterSource = @"[SqlSugar.SugarTable(""T_FILE_HEADER"")]
public class FileHeaderModel
{
    public long CID { get; set; }
    public string FileName { get; set; }
}";
                string detailSource = @"[SqlSugar.SugarTable(""T_FILE_DETAIL"")]
public class InspectionDetailModel
{
    public long CID { get; set; }
    public string Point { get; set; }
    public long? PARENT_CID { get; set; }
}";
                var script = new ParseScript
                {
                    ParserVersionId = 901,
                    IsEnabled = true,
                    ModelId = rule.ModelId,
                    TargetModelType = rule.TargetModelType,
                    ScriptCode = new MappingScriptGenerator().Generate(rule),
                    RuleType = ParseRuleType.Mapping,
                    ContentSha256 = MappingRuleSerializer.Sha256(definitionJson),
                    ModelSchemaHash = rule.ModelSchemaHash,
                    DefinitionJson = definitionJson,
                    ModelSnapshots = new List<ParseScriptModelSnapshot>
                    {
                        Snapshot(11, "FileHeaderModel", "header-schema", masterSource),
                        Snapshot(12, "InspectionDetailModel", "detail-schema", detailSource)
                    }
                };

                object raw = ScriptEngine.Execute(script, path, 1);
                MasterDetailParseResult result = MappingResultNormalizer.NormalizeMasterDetail(
                    raw,
                    "FileHeaderModel",
                    "InspectionDetailModel");

                Assert.Equal("FileHeaderModel", result.Master.GetType().Name);
                Assert.Single(result.Details);
                Assert.Equal("InspectionDetailModel", result.Details[0].GetType().Name);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }

        private static ParseScriptModelSnapshot Snapshot(
            int modelId,
            string modelType,
            string schemaHash,
            string source)
        {
            return new ParseScriptModelSnapshot
            {
                ModelId = modelId,
                ModelType = modelType,
                ModelSchemaHash = schemaHash,
                GeneratedModelCodeSnapshot = source,
                GeneratedModelCodeSha256 = MappingRuleSerializer.Sha256(source)
            };
        }

        private static MappingRuleDefinition CreateRule()
        {
            return new MappingRuleDefinition
            {
                RuleName = "file-header-details",
                ModelId = 11,
                TargetModelType = "FileHeaderModel",
                ModelSchemaHash = "header-schema",
                NormalizedExtension = ".xlsx",
                SheetName = "Data",
                RecordMode = MappingRecordMode.MasterDetail,
                Fields = new List<FieldMappingRule>(),
                MasterDetail = new MasterDetailMappingDefinition
                {
                    ParentCidField = "PARENT_CID",
                    FileName = new FileNameExtractionDefinition { ExpectedSegmentCount = 2 },
                    Master = new MappingTargetDefinition
                    {
                        ModelId = 11,
                        TargetModelType = "FileHeaderModel",
                        ModelSchemaHash = "header-schema",
                        Fields = new List<FieldMappingRule>
                        {
                            new FieldMappingRule
                            {
                                TargetField = "FileName",
                                TargetType = "string",
                                IsRequired = true,
                                Scope = MappingFieldScope.Common,
                                Locator = new MappingLocator { Type = "fileNameFull" },
                                Transforms = new List<string> { "trim" }
                            }
                        }
                    },
                    Detail = new MappingTargetDefinition
                    {
                        ModelId = 12,
                        TargetModelType = "InspectionDetailModel",
                        ModelSchemaHash = "detail-schema",
                        RepeatedRows = new RepeatedRowDefinition
                        {
                            AnchorMode = MappingTableAnchorMode.FixedCell,
                            AnchorCell = "A1",
                            FirstDataRowOffset = 1,
                            KeyColumnOffset = 0,
                            FirstColumnOffset = 0,
                            LastColumnOffset = 1,
                            StopOnBlankKey = true
                        },
                        Fields = new List<FieldMappingRule>
                        {
                            new FieldMappingRule
                            {
                                TargetField = "Point",
                                TargetType = "string",
                                IsRequired = true,
                                Scope = MappingFieldScope.RowColumn,
                                Locator = new MappingLocator
                                {
                                    Type = "rowColumn",
                                    ColumnOffset = 0,
                                    Text = "Point"
                                }
                            }
                        }
                    }
                }
            };
        }

        private sealed class FileHeaderModel
        {
            public FileHeaderModel() { }
            public string FileName { get; set; }
        }

        private sealed class InspectionDetailModel
        {
            public InspectionDetailModel() { }
            public string Point { get; set; }
            public long? PARENT_CID { get; set; }
        }
    }
}
