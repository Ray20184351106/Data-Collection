using System.Collections.Generic;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class AiMappingResponseValidatorTests
    {
        [Fact]
        public void Validate_rejects_a_suggestion_for_an_unknown_target_field()
        {
            AiMappingValidationResult result = Validate(NewResponse(
                new AiMappingSuggestion
                {
                    TargetField = "UnexpectedField",
                    Locator = Cell("A1")
                }));

            Assert.False(result.IsValid);
            Assert.Contains("UNKNOWN_TARGET", result.ErrorCodes);
        }

        [Fact]
        public void Validate_rejects_a_locator_outside_the_declarative_whitelist()
        {
            AiMappingValidationResult result = Validate(NewResponse(
                new AiMappingSuggestion
                {
                    TargetField = "SerialNumber",
                    Locator = new MappingLocator { Type = "csharp", Text = "return File.ReadAllText(path);" }
                }));

            Assert.False(result.IsValid);
            Assert.Contains("INVALID_LOCATOR", result.ErrorCodes);
        }

        [Fact]
        public void Validate_rejects_script_code_even_when_the_suggestions_are_otherwise_valid()
        {
            AiMappingResponse response = NewResponse(new AiMappingSuggestion
            {
                TargetField = "SerialNumber",
                Locator = Cell("A1")
            });
            response.ScriptCode = "System.IO.File.Delete(path);";

            AiMappingValidationResult result = Validate(response);

            Assert.False(result.IsValid);
            Assert.Contains("SCRIPT_CODE_FORBIDDEN", result.ErrorCodes);
        }

        [Fact]
        public void Validate_rejects_suggestions_created_for_an_old_model_schema()
        {
            AiMappingResponse response = NewResponse(new AiMappingSuggestion
            {
                TargetField = "SerialNumber",
                Locator = Cell("A1")
            });
            response.ModelSchemaHash = "stale-schema";

            AiMappingValidationResult result = Validate(response);

            Assert.False(result.IsValid);
            Assert.Empty(result.AcceptedSuggestions);
            Assert.Contains("MODEL_SCHEMA_DRIFT", result.ErrorCodes);
        }

        [Fact]
        public void Validate_rejects_duplicate_targets_instead_of_applying_the_last_suggestion()
        {
            AiMappingValidationResult result = Validate(NewResponse(
                new AiMappingSuggestion
                {
                    TargetField = "SerialNumber",
                    Locator = Cell("A1")
                },
                new AiMappingSuggestion
                {
                    TargetField = "SerialNumber",
                    Locator = Cell("B2")
                }));

            Assert.False(result.IsValid);
            Assert.Contains("DUPLICATE_TARGET", result.ErrorCodes);
        }

        [Fact]
        public void Validate_rejects_a_transform_outside_the_declarative_whitelist()
        {
            AiMappingValidationResult result = Validate(NewResponse(
                new AiMappingSuggestion
                {
                    TargetField = "SerialNumber",
                    Locator = Cell("A1"),
                    Transforms = new List<string> { "csharp: return File.ReadAllText(path);" }
                }));

            Assert.False(result.IsValid);
            Assert.Empty(result.AcceptedSuggestions);
            Assert.Contains("INVALID_TRANSFORM", result.ErrorCodes);
        }

        [Fact]
        public void Validate_rejects_an_out_of_range_fixed_cell_anchor()
        {
            MappingLocator locator = Cell("B2");
            locator.AnchorCell = "ZZZ999999";

            AiMappingValidationResult result = Validate(NewResponse(new AiMappingSuggestion
            {
                TargetField = "SerialNumber",
                Locator = locator
            }));

            Assert.False(result.IsValid);
            Assert.Contains("INVALID_LOCATOR", result.ErrorCodes);
        }

        [Fact]
        public void Validate_rejects_forbidden_content_in_a_fixed_cell_anchor()
        {
            MappingLocator locator = Cell("B2");
            locator.AnchorText = "DROP TABLE DataModels";

            AiMappingValidationResult result = Validate(NewResponse(new AiMappingSuggestion
            {
                TargetField = "SerialNumber",
                Locator = locator
            }));

            Assert.False(result.IsValid);
            Assert.Contains("INVALID_LOCATOR", result.ErrorCodes);
        }

        private static AiMappingValidationResult Validate(AiMappingResponse response)
        {
            return new AiMappingResponseValidator().Validate(
                response,
                new[] { "SerialNumber", "Measurement" },
                "schema-v1");
        }

        private static MappingLocator Cell(string coordinate)
        {
            return new MappingLocator
            {
                Type = "cell",
                Cell = coordinate,
                AnchorCell = "A1",
                AnchorText = "Header"
            };
        }

        private static AiMappingResponse NewResponse(params AiMappingSuggestion[] suggestions)
        {
            return new AiMappingResponse
            {
                SchemaVersion = "1",
                ModelSchemaHash = "schema-v1",
                Suggestions = new List<AiMappingSuggestion>(suggestions)
            };
        }
    }
}
