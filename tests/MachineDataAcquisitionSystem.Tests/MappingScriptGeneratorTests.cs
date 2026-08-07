using System;
using System.Collections.Generic;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class MappingScriptGeneratorTests
    {
        [Fact]
        public void Generate_is_stable_and_never_embeds_untrusted_mapping_text_as_plain_csharp()
        {
            const string maliciousText = "\"; System.IO.File.Delete(\"C:\\important.db\"); //";
            var rule = new MappingRuleDefinition
            {
                RuleName = "secure-rule",
                ModelId = 7,
                TargetModelType = "InspectionRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = ".xlsx",
                SheetName = "Data",
                Fields = new List<FieldMappingRule>
                {
                    new FieldMappingRule
                    {
                        TargetField = "SerialNumber",
                        TargetType = "string",
                        IsRequired = true,
                        Locator = new MappingLocator
                        {
                            Type = "labelOffset",
                            Text = maliciousText,
                            ColumnOffset = 1
                        },
                        Transforms = new List<string> { "trim" }
                    }
                }
            };
            var generator = new MappingScriptGenerator();

            string first = generator.Generate(rule);
            string second = generator.Generate(rule);

            Assert.Equal(first, second);
            Assert.DoesNotContain(maliciousText, first);
            Assert.DoesNotContain("File.Delete", first);
            Assert.DoesNotContain("important.db", first);
            Assert.Contains("string __mappingContract0 = model.SerialNumber;", first);
            Assert.Contains("model.SerialNumber = __mappingContract0;", first);
        }

        [Fact]
        public void Generate_rejects_a_rule_larger_than_the_runtime_limit()
        {
            var rule = new MappingRuleDefinition
            {
                RuleName = "oversized-rule",
                ModelId = 7,
                TargetModelType = "InspectionRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = ".xlsx",
                SheetName = "Data"
            };
            for (int index = 0; index < MappingRuleSerializer.MaximumMappedFields; index++)
            {
                rule.Fields.Add(new FieldMappingRule
                {
                    TargetField = "Field" + index,
                    TargetType = "string",
                    TargetDescription = new string('x', MappingRuleSerializer.MaximumRuleTextLength),
                    Locator = new MappingLocator
                    {
                        Type = "labelOffset",
                        Text = "Label" + index,
                        ColumnOffset = 1
                    }
                });
            }

            Assert.Throws<MappingValidationException>(() =>
                new MappingScriptGenerator().Generate(rule));
        }
    }
}
