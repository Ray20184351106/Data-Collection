using System;
using System.Text;
using MachineDataAcquisitionSystem.Core.Mapping;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class ParseRuleIntegrityValidatorTests
    {
        [Fact]
        public void Validate_accepts_legacy_mapping_json_missing_later_default_enum_properties()
        {
            MappingRuleDefinition definition = NewSingleRecordRule();
            string canonical = MappingRuleSerializer.Serialize(definition);
            var legacyToken = JObject.Parse(canonical);
            legacyToken.Property("RecordMode").Remove();
            legacyToken.Property("RepeatedRows").Remove();
            foreach (JObject field in (JArray)legacyToken["Fields"])
                field.Property("Scope").Remove();
            string legacyJson = legacyToken.ToString(Formatting.None);
            MappingRuleDefinition legacyDefinition = MappingRuleSerializer.Deserialize(legacyJson);
            string legacyScript = new MappingScriptGenerator().Generate(legacyDefinition).Replace(
                Convert.ToBase64String(Encoding.UTF8.GetBytes(MappingRuleSerializer.Serialize(legacyDefinition))),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(legacyJson)));
            var version = NewVersion(
                legacyJson,
                MappingRuleSerializer.Sha256(legacyJson),
                legacyScript);

            ParseRuleIntegrityValidator.Validate(version);
        }

        [Fact]
        public void Validate_rejects_other_noncanonical_mapping_json()
        {
            MappingRuleDefinition definition = NewSingleRecordRule();
            string canonical = MappingRuleSerializer.Serialize(definition);
            string noncanonical = " " + canonical;
            var version = NewVersion(
                noncanonical,
                MappingRuleSerializer.Sha256(noncanonical),
                new MappingScriptGenerator().Generate(definition));

            ParseRuleStateException error = Assert.Throws<ParseRuleStateException>(() =>
                ParseRuleIntegrityValidator.Validate(version));

            Assert.Contains("not canonical", error.Message);
        }

        [Fact]
        public void Validate_rejects_a_modified_script_even_for_supported_legacy_json()
        {
            MappingRuleDefinition definition = NewSingleRecordRule();
            string canonical = MappingRuleSerializer.Serialize(definition);
            var legacyToken = JObject.Parse(canonical);
            legacyToken.Property("RecordMode").Remove();
            legacyToken.Property("RepeatedRows").Remove();
            foreach (JObject field in (JArray)legacyToken["Fields"])
                field.Property("Scope").Remove();
            string legacyJson = legacyToken.ToString(Formatting.None);
            var version = NewVersion(
                legacyJson,
                MappingRuleSerializer.Sha256(legacyJson),
                "return null;");

            ParseRuleStateException error = Assert.Throws<ParseRuleStateException>(() =>
                ParseRuleIntegrityValidator.Validate(version));

            Assert.Contains("trusted generator", error.Message);
        }

        private static ParseRuleVersion NewVersion(string json, string hash, string script)
        {
            return new ParseRuleVersion
            {
                DefinitionId = 5,
                RuleType = ParseRuleType.Mapping,
                ModelId = 6,
                TargetModelType = "InspectionRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = ".xlsx",
                DefinitionJson = json,
                ContentSha256 = hash,
                DerivedScriptCode = script
            };
        }

        private static MappingRuleDefinition NewSingleRecordRule()
        {
            return new MappingRuleDefinition
            {
                DefinitionId = 5,
                RuleName = "inspection mapping",
                ModelId = 6,
                TargetModelType = "InspectionRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = ".xlsx",
                SheetName = "Sheet1",
                RecordMode = MappingRecordMode.SingleRecord,
                Fields =
                {
                    new FieldMappingRule
                    {
                        TargetField = "SerialNumber",
                        TargetType = "string",
                        Scope = MappingFieldScope.Common,
                        ConfirmationState = MappingConfirmationState.HumanConfirmed,
                        Locator = new MappingLocator
                        {
                            Type = "labelOffset",
                            Text = "Serial",
                            ColumnOffset = 1
                        }
                    }
                }
            };
        }
    }
}
