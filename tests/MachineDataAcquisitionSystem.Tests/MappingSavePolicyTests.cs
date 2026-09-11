using MachineDataAcquisitionSystem.Core.Mapping;
using System.Collections.Generic;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class MappingSavePolicyTests
    {
        [Fact]
        public void Summary_keeps_non_field_errors_and_limits_large_error_lists()
        {
            var rule = new MappingRuleDefinition();
            var preview = new MappingPreviewResult();
            preview.ErrorCodes.Add("CSV_ENCODING_INVALID");
            Assert.Contains("CSV_ENCODING_INVALID", MappingSavePolicy.FormatValidationFailure(rule, preview));
            rule.Fields.Add(new FieldMappingRule { TargetField = "Count", TargetType = "int", Transforms = { "integer" }, DefaultValue = "invalid" });
            for (int row = 2; row <= 11; row++)
            {
                var record = new MappingPreviewRecordResult { ExcelRowNumber = row };
                record.Fields["Count"] = new MappingPreviewFieldResult { TargetField = "Count", SourceCell = "A" + row, RawValue = "bad\nvalue", ErrorCode = "CONVERSION_FAILED" };
                preview.Records.Add(record);
            }
            string message = MappingSavePolicy.FormatValidationFailure(rule, preview);
            Assert.Contains("另有 2 项错误", message);
            Assert.Contains("bad\\nvalue", message);
            Assert.Contains("转换步骤：integer", message);
            Assert.Contains("默认值：“invalid”", message);
            Assert.DoesNotContain("第10行", message);
        }

        [Fact]
        public void Conversion_error_summary_identifies_detail_field_row_value_and_type()
        {
            var rule = new MappingRuleDefinition
            {
                RecordMode = MappingRecordMode.MasterDetail,
                MasterDetail = new MasterDetailMappingDefinition
                {
                    Master = new MappingTargetDefinition(),
                    Detail = new MappingTargetDefinition
                    {
                        Fields = { new FieldMappingRule { TargetField = "Yield", TargetType = "decimal", TargetDescription = "良品率" } }
                    }
                }
            };
            var preview = new MappingPreviewResult();
            preview.ErrorCodes.Add("CONVERSION_FAILED");
            var record = new MappingPreviewRecordResult { ExcelRowNumber = 3 };
            record.Fields["Yield"] = new MappingPreviewFieldResult
            {
                TargetField = "Yield", SourceCell = "H3", RawValue = "0.00%", ErrorCode = "CONVERSION_FAILED"
            };
            preview.Records.Add(record);
            string message = MappingSavePolicy.FormatValidationFailure(rule, preview);
            foreach (string expected in new[] { "子表", "第3行", "H3", "Yield", "良品率", "0.00%", "decimal", "无法转换" })
                Assert.Contains(expected, message);
        }

        [Theory]
        [InlineData(ParseRuleStatus.Validated)]
        [InlineData(ParseRuleStatus.Published)]
        public void Existing_validated_content_can_change_machines_without_a_sample(ParseRuleStatus status)
        {
            var version = new ParseRuleVersion { Status = status };

            Assert.False(MappingSavePolicy.RequiresSample(version, hasContentChanges: false));
        }

        [Theory]
        [InlineData(ParseRuleStatus.Draft, false)]
        [InlineData(ParseRuleStatus.Superseded, false)]
        [InlineData(ParseRuleStatus.Published, true)]
        public void Unvalidated_or_changed_content_still_requires_a_sample(
            ParseRuleStatus status,
            bool hasContentChanges)
        {
            var version = new ParseRuleVersion { Status = status };

            Assert.True(MappingSavePolicy.RequiresSample(version, hasContentChanges));
        }

        [Fact]
        public void A_new_mapping_requires_a_sample()
        {
            Assert.True(MappingSavePolicy.RequiresSample(null, hasContentChanges: false));
        }

        [Fact]
        public void Editor_prefers_published_version_over_newer_legacy_draft()
        {
            var published = new ParseRuleVersion
            {
                Id = 7,
                VersionNumber = 1,
                Status = ParseRuleStatus.Published
            };
            var legacyDraft = new ParseRuleVersion
            {
                Id = 8,
                VersionNumber = 2,
                Status = ParseRuleStatus.Draft
            };

            ParseRuleVersion selected = MappingSavePolicy.SelectPreferredEditorVersion(
                new List<ParseRuleVersion> { legacyDraft, published });

            Assert.Same(published, selected);
        }

        [Fact]
        public void Editor_uses_latest_draft_when_mapping_has_never_been_validated()
        {
            var firstDraft = new ParseRuleVersion
            {
                Id = 1,
                VersionNumber = 1,
                Status = ParseRuleStatus.Draft
            };
            var latestDraft = new ParseRuleVersion
            {
                Id = 2,
                VersionNumber = 2,
                Status = ParseRuleStatus.Draft
            };

            ParseRuleVersion selected = MappingSavePolicy.SelectPreferredEditorVersion(
                new List<ParseRuleVersion> { firstDraft, latestDraft });

            Assert.Same(latestDraft, selected);
        }
    }
}
