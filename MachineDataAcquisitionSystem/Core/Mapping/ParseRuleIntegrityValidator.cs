using System;
using Newtonsoft.Json;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    /// <summary>
    /// Verifies that immutable version content still matches the material that was
    /// originally hashed and, for declarative mappings, the trusted fixed generator.
    /// </summary>
    public static class ParseRuleIntegrityValidator
    {
        public static void Validate(ParseRuleVersion version)
        {
            if (version == null)
                throw new ArgumentNullException(nameof(version));
            if (string.IsNullOrWhiteSpace(version.ContentSha256))
                throw new ParseRuleStateException("The parse-rule content hash is missing.");

            if (version.RuleType == ParseRuleType.LegacyCode)
            {
                if (string.IsNullOrWhiteSpace(version.DerivedScriptCode) ||
                    !string.Equals(
                        MappingRuleSerializer.Sha256(version.DerivedScriptCode),
                        version.ContentSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new ParseRuleStateException("The legacy parse script content hash does not match.");
                return;
            }

            if (version.RuleType != ParseRuleType.Mapping)
                throw new ParseRuleStateException("The parse-rule type is invalid.");

            MappingRuleDefinition definition;
            try
            {
                definition = MappingRuleSerializer.Deserialize(version.DefinitionJson);
                MappingRuleSerializer.ValidateDefinition(definition);
            }
            catch (Exception ex) when (
                ex is JsonException ||
                ex is MappingValidationException ||
                ex is ArgumentException)
            {
                throw new ParseRuleStateException("The mapping definition JSON is invalid: " + ex.Message);
            }

            string canonicalJson = MappingRuleSerializer.Serialize(definition);
            bool isCanonical = string.Equals(canonicalJson, version.DefinitionJson, StringComparison.Ordinal);
            bool isSupportedLegacyCanonical = !isCanonical && string.Equals(
                MappingRuleSerializer.SerializeLegacyDefaultEnumProjection(definition),
                version.DefinitionJson,
                StringComparison.Ordinal);
            if (!isCanonical && !isSupportedLegacyCanonical)
            {
                isSupportedLegacyCanonical = string.Equals(
                    MappingRuleSerializer.SerializePreImageLegacyProjection(definition),
                    version.DefinitionJson,
                    StringComparison.Ordinal);
            }
            if (!isCanonical && !isSupportedLegacyCanonical)
                throw new ParseRuleStateException("The mapping definition JSON is not canonical or was modified.");
            if (!string.Equals(
                    MappingRuleSerializer.Sha256(version.DefinitionJson),
                    version.ContentSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new ParseRuleStateException("The mapping definition content hash does not match.");
            if (definition.DefinitionId != version.DefinitionId ||
                definition.ModelId != version.ModelId ||
                !string.Equals(definition.TargetModelType, version.TargetModelType, StringComparison.Ordinal) ||
                !string.Equals(definition.ModelSchemaHash, version.ModelSchemaHash, StringComparison.Ordinal) ||
                !string.Equals(definition.NormalizedExtension, version.NormalizedExtension, StringComparison.Ordinal))
                throw new ParseRuleStateException("The mapping definition identity does not match its version metadata.");

            var generator = new MappingScriptGenerator();
            string expectedScript = isSupportedLegacyCanonical
                ? generator.GenerateFromValidatedSerializedDefinition(definition, version.DefinitionJson)
                : generator.Generate(definition);
            if (!string.Equals(expectedScript, version.DerivedScriptCode, StringComparison.Ordinal))
                throw new ParseRuleStateException("The derived mapping script does not match the trusted generator output.");
        }
    }
}
