using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MachineDataAcquisitionSystem.Core.Mapping;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class CsvMappingPreviewServiceTests
    {
        [Fact]
        public void Inspect_preserves_quoted_delimiters_escaped_quotes_and_embedded_newlines()
        {
            string path = WriteCsv(
                "Name,Note,Description\r\n" +
                "\"Widget, A\",\"He said \"\"ok\"\"\",\"line1\r\nline2\"\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingWorkbookSnapshot snapshot = new CsvMappingPreviewService().Inspect(
                    path,
                    CsvOptions());

                MappingSheetSnapshot sheet = Assert.Single(snapshot.Sheets);
                Assert.Equal("CSV", sheet.Name);
                Assert.Equal("Widget, A", Cell(sheet, "A2").DisplayText);
                Assert.Equal("He said \"ok\"", Cell(sheet, "B2").DisplayText);
                Assert.Equal("line1\r\nline2", Cell(sheet, "C2").DisplayText);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_and_runtime_create_all_csv_rows_with_existing_mapping_contracts()
        {
            string path = WriteCsv(
                "Serial,Value\r\nSN-001,12.50\r\nSN-002,13.75\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingRuleDefinition rule = RepeatingRule();
                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, rule);

                Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.ErrorCodes));
                Assert.Equal(2, preview.Records.Count);
                Assert.Equal("SN-001", preview.Records[0].Fields["Serial"].Value);
                Assert.Equal(13.75m, preview.Records[1].Fields["Value"].Value);

                string encoded = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(MappingRuleSerializer.Serialize(rule)));
                List<CsvRuntimeRecord> models = MappingRuntime.CreateModels<CsvRuntimeRecord>(
                    encoded,
                    path,
                    1);

                Assert.Equal(2, models.Count);
                Assert.Equal("SN-002", models[1].Serial);
                Assert.Equal(13.75m, models[1].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Inspect_uses_the_explicit_gb18030_encoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string path = WriteCsv(
                "序列号,结果\r\n设备一,合格\r\n",
                Encoding.GetEncoding("GB18030"));
            try
            {
                CsvMappingOptions options = CsvOptions();
                options.EncodingName = "gb18030";

                MappingWorkbookSnapshot snapshot = new CsvMappingPreviewService().Inspect(path, options);

                MappingSheetSnapshot sheet = Assert.Single(snapshot.Sheets);
                Assert.Equal("设备一", Cell(sheet, "A2").DisplayText);
                Assert.Equal("合格", Cell(sheet, "B2").DisplayText);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_does_not_override_the_saved_encoding_from_a_file_bom()
        {
            string path = WriteCsv(
                "序列号,Value\r\n设备一,1.5\r\n",
                new UTF8Encoding(true));
            try
            {
                MappingRuleDefinition rule = RepeatingRule();
                rule.CsvOptions.EncodingName = "gbk";
                rule.RepeatedRows.AnchorText = "序列号";
                rule.Fields[0].Locator.Text = "序列号";

                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, rule);

                Assert.False(preview.IsValid);
                Assert.Contains("CSV_ENCODING_INVALID", preview.ErrorCodes);
                Assert.Empty(preview.Records);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Inspect_accepts_a_utf8_bom_when_utf8_is_selected()
        {
            string path = WriteCsv(
                "序列号,结果\r\n设备一,合格\r\n",
                new UTF8Encoding(true));
            try
            {
                MappingWorkbookSnapshot snapshot = new CsvMappingPreviewService().Inspect(
                    path,
                    CsvOptions());

                MappingSheetSnapshot sheet = Assert.Single(snapshot.Sheets);
                Assert.Equal("序列号", Cell(sheet, "A1").DisplayText);
                Assert.Equal("设备一", Cell(sheet, "A2").DisplayText);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_rejects_malformed_csv_without_returning_partial_records()
        {
            string path = WriteCsv(
                "Serial,Value\r\n\"SN-001,12.50\r\nSN-002,13.75\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, RepeatingRule());

                Assert.False(preview.IsValid);
                Assert.Contains("INVALID_CSV", preview.ErrorCodes);
                Assert.Empty(preview.Records);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_rejects_rows_with_a_different_column_count()
        {
            string path = WriteCsv(
                "Serial,Value\r\nSN-001,12.50\r\nSN-002\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, RepeatingRule());

                Assert.False(preview.IsValid);
                Assert.Contains("CSV_COLUMN_COUNT_MISMATCH", preview.ErrorCodes);
                Assert.Empty(preview.Records);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Inspect_preserves_empty_field_positions_including_the_trailing_field()
        {
            string path = WriteCsv(
                "First,Second,Third,Fourth\r\n" +
                "A,,C,\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingWorkbookSnapshot snapshot = new CsvMappingPreviewService().Inspect(path, CsvOptions());

                MappingSheetSnapshot sheet = Assert.Single(snapshot.Sheets);
                Assert.Equal("A", Cell(sheet, "A2").DisplayText);
                Assert.Equal("C", Cell(sheet, "C2").DisplayText);
                Assert.DoesNotContain(sheet.Cells, cell => cell.Coordinate == "B2");
                Assert.DoesNotContain(sheet.Cells, cell => cell.Coordinate == "D2");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_rejects_column_and_cell_length_limits()
        {
            string tooManyColumns = string.Join(",", Enumerable.Range(1, 129).Select(index => "H" + index)) +
                "\r\n" + string.Join(",", Enumerable.Repeat("1", 129)) + "\r\n";
            string longCell = "Serial,Value\r\nSN-1," +
                new string('x', ExcelMappingPreviewService.MaximumCellTextLength + 1) + "\r\n";
            string columnPath = WriteCsv(tooManyColumns, new UTF8Encoding(false));
            string cellPath = WriteCsv(longCell, new UTF8Encoding(false));
            try
            {
                MappingPreviewResult columns = new CsvMappingPreviewService().Preview(columnPath, RepeatingRule());
                MappingPreviewResult cell = new CsvMappingPreviewService().Preview(cellPath, RepeatingRule());

                Assert.Contains("COLUMN_COUNT_LIMIT", columns.ErrorCodes);
                Assert.Contains("CELL_TEXT_LIMIT", cell.ErrorCodes);
            }
            finally
            {
                File.Delete(columnPath);
                File.Delete(cellPath);
            }
        }

        [Fact]
        public void Csv_rules_require_csv_options_and_excel_canonical_json_stays_unchanged()
        {
            MappingRuleDefinition csv = RepeatingRule();
            csv.CsvOptions = null;
            MappingValidationException missing = Assert.Throws<MappingValidationException>(() =>
                MappingRuleSerializer.ValidateDefinition(csv));
            Assert.Contains("CSV", missing.Message);

            MappingRuleDefinition excel = RepeatingRule();
            excel.NormalizedExtension = ".xlsx";
            excel.CsvOptions = null;
            string json = MappingRuleSerializer.Serialize(excel);

            Assert.Null(JObject.Parse(json).Property("CsvOptions"));
            MappingRuleSerializer.ValidateDefinition(excel);
        }

        [Fact]
        public void Preview_single_record_reads_values_below_csv_headers()
        {
            string path = WriteCsv(
                "Serial,Result\r\nSN-100,OK\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingRuleDefinition rule = RepeatingRule();
                rule.RecordMode = MappingRecordMode.SingleRecord;
                rule.RepeatedRows = null;
                rule.Fields = new List<FieldMappingRule>
                {
                    Common("Serial", "Serial"),
                    Common("Result", "Result")
                };

                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, rule);

                Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.ErrorCodes));
                Assert.Equal("SN-100", preview.Fields["Serial"].Value);
                Assert.Equal("OK", preview.Fields["Result"].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_single_record_uses_the_configured_header_row_for_header_locators()
        {
            string path = WriteCsv(
                "Serial\r\n" +
                "Serial,Result\r\n" +
                "SN-101,OK\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingRuleDefinition rule = RepeatingRule();
                rule.RecordMode = MappingRecordMode.SingleRecord;
                rule.RepeatedRows = null;
                rule.CsvOptions.HeaderRowNumber = 2;
                rule.CsvOptions.FirstDataRowNumber = 3;
                rule.Fields = new List<FieldMappingRule>
                {
                    Common("Serial", "Serial"),
                    Common("Result", "Result")
                };

                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, rule);

                Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.ErrorCodes));
                Assert.Equal("SN-101", preview.Fields["Serial"].Value);
                Assert.Equal("OK", preview.Fields["Result"].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_master_detail_uses_the_original_csv_file_name_for_master_fields()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CsvMapping_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "MachineA[LOT-88].csv");
            File.WriteAllText(path, "Serial,Value\r\nSN-1,2.5\r\n", new UTF8Encoding(false));
            try
            {
                MappingRuleDefinition rule = MasterDetailRule();

                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, rule);

                Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.ErrorCodes));
                Assert.Equal("LOT-88", preview.Fields["LotNumber"].Value);
                Assert.Single(preview.Records);
                Assert.Equal("SN-1", preview.Records[0].Fields["Serial"].Value);
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
        }

        [Fact]
        public void Preview_uses_custom_delimiter_header_row_and_first_data_row()
        {
            string path = WriteCsv(
                "设备导出报表\r\n" +
                "Serial;Value\r\n" +
                "说明行\r\n" +
                "SN-200;18.25\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingRuleDefinition rule = RepeatingRule();
                rule.CsvOptions.Delimiter = ";";
                rule.CsvOptions.HeaderRowNumber = 2;
                rule.CsvOptions.FirstDataRowNumber = 4;
                rule.RepeatedRows.AnchorCell = "A2";
                rule.RepeatedRows.FirstDataRowOffset = 2;

                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, rule);

                Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.ErrorCodes));
                MappingPreviewRecordResult record = Assert.Single(preview.Records);
                Assert.Equal(4, record.ExcelRowNumber);
                Assert.Equal("SN-200", record.Fields["Serial"].Value);
                Assert.Equal(18.25m, record.Fields["Value"].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_uses_the_configured_header_row_when_metadata_repeats_the_anchor_text()
        {
            string path = WriteCsv(
                "Serial\r\n" +
                "Serial,Value\r\n" +
                "SN-300,22.5\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingRuleDefinition rule = RepeatingRule();
                rule.CsvOptions.HeaderRowNumber = 2;
                rule.CsvOptions.FirstDataRowNumber = 3;
                rule.RepeatedRows.AnchorCell = "A2";

                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, rule);

                Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.ErrorCodes));
                Assert.Equal("SN-300", Assert.Single(preview.Records).Fields["Serial"].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_rejects_a_repeating_row_offset_that_disagrees_with_csv_options()
        {
            string path = WriteCsv(
                "Serial,Value\r\n" +
                "说明,0\r\n" +
                "SN-400,8.5\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingRuleDefinition rule = RepeatingRule();
                rule.CsvOptions.FirstDataRowNumber = 3;

                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, rule);

                Assert.False(preview.IsValid);
                Assert.Contains("CSV_ROW_CONFIGURATION_MISMATCH", preview.ErrorCodes);
                Assert.Empty(preview.Records);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("Serial,,Value\r\nSN-1,OK,1\r\n", "EMPTY_CSV_HEADER")]
        [InlineData("Serial,serial\r\nSN-1,OK\r\n", "DUPLICATE_CSV_HEADER")]
        public void Preview_rejects_empty_or_duplicate_headers(string content, string expectedCode)
        {
            string path = WriteCsv(content, new UTF8Encoding(false));
            try
            {
                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, RepeatingRule());

                Assert.False(preview.IsValid);
                Assert.Contains(expectedCode, preview.ErrorCodes);
                Assert.Empty(preview.Records);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_rejects_bytes_that_are_not_valid_for_the_selected_utf8_encoding()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "CsvMapping_" + Guid.NewGuid().ToString("N") + ".csv");
            byte[] prefix = Encoding.ASCII.GetBytes("Serial,Value\r\nSN-");
            byte[] suffix = Encoding.ASCII.GetBytes(",12.5\r\n");
            File.WriteAllBytes(path, prefix.Concat(new byte[] { 0xC3, 0x28 }).Concat(suffix).ToArray());
            try
            {
                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, RepeatingRule());

                Assert.False(preview.IsValid);
                Assert.Contains("CSV_ENCODING_INVALID", preview.ErrorCodes);
                Assert.Empty(preview.Records);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_skips_fully_blank_rows_without_losing_later_records()
        {
            string path = WriteCsv(
                "Serial,Value\r\n" +
                "SN-001,12.50\r\n" +
                ",\r\n" +
                "SN-002,13.75\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, RepeatingRule());

                Assert.True(preview.IsValid, string.Join(Environment.NewLine, preview.ErrorCodes));
                Assert.Equal(2, preview.Records.Count);
                Assert.Equal(new[] { 2, 4 }, preview.Records.Select(record => record.ExcelRowNumber).ToArray());
                Assert.Equal("SN-002", preview.Records[1].Fields["Serial"].Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Preview_rejects_a_nonblank_row_whose_mapped_key_is_empty()
        {
            string path = WriteCsv(
                "Serial,Value\r\n" +
                ",12.50\r\n" +
                "SN-002,13.75\r\n",
                new UTF8Encoding(false));
            try
            {
                MappingPreviewResult preview = new CsvMappingPreviewService().Preview(path, RepeatingRule());

                Assert.False(preview.IsValid);
                Assert.Contains("MISSING_ROW_KEY", preview.ErrorCodes);
                Assert.Empty(preview.Records);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Template_signature_ignores_data_values_but_changes_with_the_header()
        {
            string firstPath = WriteCsv("Serial,Value\r\nSN-001,1\r\n", new UTF8Encoding(false));
            string secondPath = WriteCsv("Serial,Value\r\nSN-999,500\r\n", new UTF8Encoding(false));
            string changedHeaderPath = WriteCsv("Serial,Reading\r\nSN-001,1\r\n", new UTF8Encoding(false));
            try
            {
                var service = new CsvMappingPreviewService();
                string first = service.Preview(firstPath, RepeatingRule()).TemplateSignature;
                string second = service.Preview(secondPath, RepeatingRule()).TemplateSignature;
                string changedHeader = service.Preview(changedHeaderPath, RepeatingRule()).TemplateSignature;

                Assert.False(string.IsNullOrWhiteSpace(first));
                Assert.Equal(first, second);
                Assert.NotEqual(first, changedHeader);
            }
            finally
            {
                File.Delete(firstPath);
                File.Delete(secondPath);
                File.Delete(changedHeaderPath);
            }
        }

        [Fact]
        public void Template_signature_changes_when_the_saved_encoding_changes()
        {
            string path = WriteCsv("Serial,Value\r\nSN-001,1\r\n", new UTF8Encoding(false));
            try
            {
                MappingRuleDefinition utf8Rule = RepeatingRule();
                MappingRuleDefinition gbkRule = RepeatingRule();
                gbkRule.CsvOptions.EncodingName = "gbk";
                var service = new CsvMappingPreviewService();

                MappingPreviewResult utf8 = service.Preview(path, utf8Rule);
                MappingPreviewResult gbk = service.Preview(path, gbkRule);

                Assert.True(utf8.IsValid);
                Assert.True(gbk.IsValid);
                Assert.NotEqual(utf8.TemplateSignature, gbk.TemplateSignature);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Csv_rule_options_survive_save_validate_publish_and_lookup()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "CsvRuleStore_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var store = new ParseRuleStore(databasePath))
                {
                    store.Initialize();
                    MappingRuleDefinition definition = RepeatingRule();
                    definition.CsvOptions.EncodingName = "gb18030";
                    definition.CsvOptions.Delimiter = ";";

                    ParseRuleVersion draft = store.SaveDraft(definition);
                    ParseRuleVersion validated = store.Validate(
                        draft.Id,
                        draft.Revision,
                        "csv sample passed");
                    ParseRuleVersion published = store.Publish(
                        validated.Id,
                        "MachineCsv",
                        validated.Revision);
                    ParseRuleVersion loaded = store.GetPublished("MachineCsv", ".CSV");

                    Assert.Equal(published.Id, loaded.Id);
                    Assert.Equal(".csv", loaded.NormalizedExtension);
                    Assert.Equal(MappingRuleSerializer.Sha256(loaded.DefinitionJson), loaded.ContentSha256);
                    MappingRuleDefinition roundTrip = MappingRuleSerializer.Deserialize(loaded.DefinitionJson);
                    Assert.Equal("gb18030", roundTrip.CsvOptions.EncodingName);
                    Assert.Equal(";", roundTrip.CsvOptions.Delimiter);
                    Assert.Contains("MappingRuntime.CreateModels", loaded.DerivedScriptCode);
                    ParseRuleIntegrityValidator.Validate(loaded);
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        [Fact]
        public void Local_assistant_uses_csv_headers_and_the_configured_first_data_row()
        {
            string path = WriteCsv(
                "设备导出\r\n" +
                "Serial,Value\r\n" +
                "说明行\r\n" +
                "SN-500,3.25\r\n",
                new UTF8Encoding(false));
            try
            {
                CsvMappingOptions options = CsvOptions();
                options.HeaderRowNumber = 2;
                options.FirstDataRowNumber = 4;
                MappingWorkbookSnapshot snapshot = new CsvMappingPreviewService().Inspect(path, options);

                AiMappingSuggestion suggestion = Assert.Single(new LocalMappingAssistant().Suggest(
                    snapshot,
                    new[] { new AiTargetField { FieldName = "Serial" } },
                    options));

                Assert.Equal("headerColumn", suggestion.Locator.Type);
                Assert.Equal("Serial", suggestion.Locator.Text);
                Assert.Equal(2, suggestion.Locator.DataRowOffset);
                Assert.Equal(0, suggestion.Locator.ColumnOffset);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Local_assistant_keeps_the_existing_excel_label_offset_behavior()
        {
            var snapshot = new MappingWorkbookSnapshot
            {
                FileExtension = ".xlsx",
                Sheets = new List<MappingSheetSnapshot>
                {
                    new MappingSheetSnapshot
                    {
                        Name = "Data",
                        Cells = new List<MappingCellSnapshot>
                        {
                            new MappingCellSnapshot { Coordinate = "A1", DisplayText = "Serial" }
                        }
                    }
                }
            };

            AiMappingSuggestion suggestion = Assert.Single(new LocalMappingAssistant().Suggest(
                snapshot,
                new[] { new AiTargetField { FieldName = "Serial" } }));

            Assert.Equal("labelOffset", suggestion.Locator.Type);
            Assert.Equal(1, suggestion.Locator.ColumnOffset);
            Assert.Equal(0, suggestion.Locator.DataRowOffset);
        }

        private static MappingRuleDefinition RepeatingRule()
        {
            return new MappingRuleDefinition
            {
                RuleName = "csv-measurements",
                ModelId = 7,
                TargetModelType = "CsvRuntimeRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = ".csv",
                SheetName = "CSV",
                CsvOptions = CsvOptions(),
                RecordMode = MappingRecordMode.RepeatingRows,
                RepeatedRows = new RepeatedRowDefinition
                {
                    AnchorMode = MappingTableAnchorMode.HeaderText,
                    AnchorText = "Serial",
                    AnchorCell = "A1",
                    FirstDataRowOffset = 1,
                    KeyColumnOffset = 0,
                    FirstColumnOffset = 0,
                    LastColumnOffset = 1,
                    StopOnBlankKey = true
                },
                Fields = new List<FieldMappingRule>
                {
                    Row("Serial", "string", 0, "Serial", "trim"),
                    Row("Value", "decimal", 1, "Value", "decimal")
                }
            };
        }

        private static CsvMappingOptions CsvOptions()
        {
            return new CsvMappingOptions
            {
                EncodingName = "utf-8",
                Delimiter = ",",
                QuoteCharacter = '"',
                HeaderRowNumber = 1,
                FirstDataRowNumber = 2,
                SkipBlankRows = true
            };
        }

        private static FieldMappingRule Row(
            string target,
            string type,
            int columnOffset,
            string header,
            params string[] transforms)
        {
            return new FieldMappingRule
            {
                TargetField = target,
                TargetType = type,
                IsRequired = true,
                Scope = MappingFieldScope.RowColumn,
                Locator = new MappingLocator
                {
                    Type = "rowColumn",
                    Text = header,
                    ColumnOffset = columnOffset
                },
                Transforms = new List<string>(transforms),
                ConfirmationState = MappingConfirmationState.HumanConfirmed
            };
        }

        private static FieldMappingRule Common(string target, string header)
        {
            return new FieldMappingRule
            {
                TargetField = target,
                TargetType = "string",
                IsRequired = true,
                Scope = MappingFieldScope.Common,
                Locator = new MappingLocator
                {
                    Type = "headerColumn",
                    Text = header,
                    DataRowOffset = 1
                },
                Transforms = new List<string> { "trim" },
                ConfirmationState = MappingConfirmationState.HumanConfirmed
            };
        }

        private static MappingRuleDefinition MasterDetailRule()
        {
            return new MappingRuleDefinition
            {
                RuleName = "csv-master-detail",
                ModelId = 8,
                TargetModelType = "CsvMasterRecord",
                ModelSchemaHash = "master-v1",
                NormalizedExtension = ".csv",
                SheetName = "CSV",
                CsvOptions = CsvOptions(),
                RecordMode = MappingRecordMode.MasterDetail,
                MasterDetail = new MasterDetailMappingDefinition
                {
                    FileName = new FileNameExtractionDefinition { ExpectedSegmentCount = 1 },
                    ParentCidField = "PARENT_CID",
                    Master = new MappingTargetDefinition
                    {
                        ModelId = 8,
                        TargetModelType = "CsvMasterRecord",
                        ModelSchemaHash = "master-v1",
                        Fields = new List<FieldMappingRule>
                        {
                            new FieldMappingRule
                            {
                                TargetField = "LotNumber",
                                TargetType = "string",
                                IsRequired = true,
                                Scope = MappingFieldScope.Common,
                                Locator = new MappingLocator
                                {
                                    Type = "fileNameSegment",
                                    SegmentIndex = 0
                                },
                                Transforms = new List<string> { "trim" },
                                ConfirmationState = MappingConfirmationState.HumanConfirmed
                            }
                        }
                    },
                    Detail = new MappingTargetDefinition
                    {
                        ModelId = 9,
                        TargetModelType = "CsvDetailRecord",
                        ModelSchemaHash = "detail-v1",
                        RepeatedRows = RepeatingRule().RepeatedRows,
                        Fields = RepeatingRule().Fields
                    }
                }
            };
        }

        private static MappingCellSnapshot Cell(MappingSheetSnapshot sheet, string coordinate)
        {
            return sheet.Cells.Single(cell => string.Equals(
                cell.Coordinate,
                coordinate,
                StringComparison.Ordinal));
        }

        private static string WriteCsv(string content, Encoding encoding)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "CsvMapping_" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(path, content, encoding);
            return path;
        }

        public sealed class CsvRuntimeRecord
        {
            public string Serial { get; set; }
            public decimal Value { get; set; }
        }

        public sealed class CsvMasterRecord
        {
            public string LotNumber { get; set; }
        }

        public sealed class CsvDetailRecord
        {
            public string Serial { get; set; }
            public decimal Value { get; set; }
            public long? PARENT_CID { get; set; }
        }
    }
}
