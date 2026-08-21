using System;
using System.Collections.Generic;
using System.Text;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class MappingScriptGenerator
    {
        public string Generate(MappingRuleDefinition rule)
        {
            MappingRuleSerializer.ValidateDefinition(rule);
            string json = MappingRuleSerializer.Serialize(rule);
            return GenerateCore(rule, json);
        }

        internal string GenerateFromValidatedSerializedDefinition(
            MappingRuleDefinition rule,
            string serializedDefinition)
        {
            MappingRuleSerializer.ValidateDefinition(rule);
            if (string.IsNullOrWhiteSpace(serializedDefinition))
                throw new ArgumentException("Serialized mapping definition is required.", nameof(serializedDefinition));
            MappingRuleSerializer.ValidateSerializedSize(serializedDefinition);
            return GenerateCore(rule, serializedDefinition);
        }

        private static string GenerateCore(MappingRuleDefinition rule, string json)
        {
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            bool repeating = rule.RecordMode == MappingRecordMode.RepeatingRows;
            string contractModel = repeating ? "__mappingContractModel" : "model";
            var lines = new List<string>
            {
                "var " + contractModel + " = new " + rule.TargetModelType + "();"
            };
            for (int index = 0; index < rule.Fields.Count; index++)
            {
                FieldMappingRule field = rule.Fields[index];
                string contractVariable = "__mappingContract" + index;
                string csharpType = ToCSharpType(field.TargetType);
                if (!field.IsRequired && !string.Equals(csharpType, "string", StringComparison.Ordinal))
                {
                    csharpType += "?";
                }
                // These trusted, identifier-only assignments force the generated model
                // source to expose a readable/writable property with the DB-declared type.
                lines.Add(csharpType + " " + contractVariable + " = " + contractModel + "." + field.TargetField + ";");
                lines.Add(contractModel + "." + field.TargetField + " = " + contractVariable + ";");
            }
            if (repeating)
            {
                lines.Add(
                    "var models = MachineDataAcquisitionSystem.Core.Mapping.MappingRuntime.CreateModels<" +
                    rule.TargetModelType + ">(\"" + encoded + "\", filePath, machineId);");
                lines.Add("return models;");
            }
            else
            {
                lines.Add(
                    "MachineDataAcquisitionSystem.Core.Mapping.MappingRuntime.PopulateModel(" +
                    "model, \"" + encoded + "\", filePath, machineId);");
                lines.Add("return model;");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string ToCSharpType(string targetType)
        {
            switch ((targetType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "string": return "string";
                case "int": return "int";
                case "long": return "long";
                case "decimal": return "decimal";
                case "float": return "float";
                case "double": return "double";
                case "datetime": return "DateTime";
                case "bool": return "bool";
                default:
                    throw new MappingValidationException("目标字段类型不受支持：" + targetType);
            }
        }
    }
}
