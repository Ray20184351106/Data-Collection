using System;
using System.Collections.Generic;
using System.Linq;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class AiMappingResponseValidator
    {
        private const int MaximumSuggestions = 512;

        public AiMappingValidationResult Validate(
            AiMappingResponse response,
            IEnumerable<string> allowedTargets,
            string expectedModelSchemaHash)
        {
            var result = new AiMappingValidationResult();
            var targets = new HashSet<string>(allowedTargets ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            if (response == null)
            {
                AddError(result, "INVALID_RESPONSE");
                return Complete(result);
            }
            if (!string.Equals(response.SchemaVersion, "1", StringComparison.Ordinal))
                AddError(result, "UNSUPPORTED_SCHEMA_VERSION");
            if (!string.Equals(response.ModelSchemaHash, expectedModelSchemaHash, StringComparison.Ordinal))
                AddError(result, "MODEL_SCHEMA_DRIFT");
            if (!string.IsNullOrWhiteSpace(response.ScriptCode))
                AddError(result, "SCRIPT_CODE_FORBIDDEN");

            List<AiMappingSuggestion> suggestions = response.Suggestions ?? new List<AiMappingSuggestion>();
            if (suggestions.Count > MaximumSuggestions)
                AddError(result, "TOO_MANY_SUGGESTIONS");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AiMappingSuggestion suggestion in suggestions.Take(MaximumSuggestions))
            {
                if (suggestion == null || !targets.Contains(suggestion.TargetField ?? string.Empty))
                {
                    AddError(result, "UNKNOWN_TARGET");
                    continue;
                }
                if (!seen.Add(suggestion.TargetField))
                {
                    AddError(result, "DUPLICATE_TARGET");
                    continue;
                }
                if (!IsValidLocator(suggestion.Locator))
                {
                    AddError(result, "INVALID_LOCATOR");
                    continue;
                }
                if ((suggestion.Transforms ?? new List<string>()).Any(
                    transform => !MappingRuleSerializer.AllowedTransforms.Contains(transform ?? string.Empty) ||
                                 string.Equals(transform, "valueMap", StringComparison.Ordinal) ||
                                 string.Equals(transform, "default", StringComparison.Ordinal)) ||
                    (suggestion.Transforms ?? new List<string>())
                        .Distinct(StringComparer.Ordinal).Count() !=
                    (suggestion.Transforms ?? new List<string>()).Count)
                {
                    AddError(result, "INVALID_TRANSFORM");
                    continue;
                }
                if (suggestion.Confidence < 0m || suggestion.Confidence > 1m)
                {
                    AddError(result, "INVALID_CONFIDENCE");
                    continue;
                }
                if ((suggestion.Reason ?? string.Empty).Length > 512 ||
                    ContainsForbiddenContent(suggestion.Reason) ||
                    ContainsForbiddenContent(suggestion.Locator.Text))
                {
                    AddError(result, "FORBIDDEN_CONTENT");
                    continue;
                }
                result.AcceptedSuggestions.Add(suggestion);
            }

            if ((response.Assumptions ?? new List<string>()).Any(item =>
                item != null && (item.Length > 512 || ContainsForbiddenContent(item))))
                AddError(result, "FORBIDDEN_CONTENT");

            List<string> unmappedTargets = response.UnmappedTargets ?? new List<string>();
            if (unmappedTargets.Any(target =>
                    string.IsNullOrWhiteSpace(target) ||
                    !targets.Contains(target) ||
                    ContainsForbiddenContent(target)) ||
                unmappedTargets.Distinct(StringComparer.Ordinal).Count() != unmappedTargets.Count)
                AddError(result, "INVALID_UNMAPPED_TARGET");

            return Complete(result);
        }

        private static bool IsValidLocator(MappingLocator locator)
        {
            if (locator == null || !MappingRuleSerializer.AllowedLocatorTypes.Contains(locator.Type ?? string.Empty))
                return false;
            try
            {
                MappingRuleSerializer.ValidateLocator(locator);
                switch (locator.Type)
                {
                    case "cell":
                        return IsSafeCoordinate(locator.Cell) &&
                               IsSafeCoordinate(locator.AnchorCell) &&
                               IsSafeAnchorText(locator.AnchorText);
                    case "labelOffset":
                        return IsSafeAnchorText(locator.Text);
                    case "rowKey":
                        return IsSafeAnchorText(locator.Text) && IsSafeColumn(locator.ValueColumn);
                    case "headerColumn":
                        return IsSafeAnchorText(locator.Text) && locator.DataRowOffset != 0;
                    default:
                        return false;
                }
            }
            catch (MappingValidationException)
            {
                return false;
            }
        }

        private static bool IsSafeAnchorText(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !ContainsForbiddenContent(value);
        }

        private static bool IsSafeCoordinate(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 8) return false;
            string coordinate = value.Trim().ToUpperInvariant();
            int split = 0;
            while (split < coordinate.Length && coordinate[split] >= 'A' && coordinate[split] <= 'Z') split++;
            if (split == 0 || split == coordinate.Length) return false;
            int column = 0;
            for (int index = 0; index < split; index++)
            {
                column = column * 26 + coordinate[index] - 'A' + 1;
                if (column > ExcelMappingPreviewService.MaximumColumns) return false;
            }
            int row;
            return int.TryParse(coordinate.Substring(split), out row) && row >= 1 && row <= ExcelMappingPreviewService.MaximumRows;
        }

        private static bool IsSafeColumn(string value)
        {
            return IsSafeCoordinate((value ?? string.Empty) + "1");
        }

        private static bool ContainsForbiddenContent(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string text = value.ToLowerInvariant();
            string[] forbidden =
            {
                "system.", "file.", "directory.", "process.", "return ",
                "select ", "insert ", "update ", "delete ", "drop ", "alter ",
                "../", "..\\", "c:\\", "\\\\", "file://", "http://", "https://"
            };
            return forbidden.Any(text.Contains);
        }

        private static void AddError(AiMappingValidationResult result, string code)
        {
            if (!result.ErrorCodes.Contains(code))
                result.ErrorCodes.Add(code);
        }

        private static AiMappingValidationResult Complete(AiMappingValidationResult result)
        {
            result.IsValid = result.ErrorCodes.Count == 0;
            if (!result.IsValid)
                result.AcceptedSuggestions.Clear();
            return result;
        }
    }
}
