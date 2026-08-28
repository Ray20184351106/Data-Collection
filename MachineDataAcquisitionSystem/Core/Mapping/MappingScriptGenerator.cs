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

            if (rule.RecordMode == MappingRecordMode.MasterDetail)
                return GenerateMasterDetail(rule, encoded);

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

        private static string GenerateMasterDetail(MappingRuleDefinition rule, string encoded)
        {
            MasterDetailMappingDefinition definition = rule.MasterDetail;
            var lines = new List<string>
            {
                "var __mappingMasterContract = new " + definition.Master.TargetModelType + "();",
                "var __mappingDetailContract = new " + definition.Detail.TargetModelType + "();"
            };
            AddContractChecks(lines, definition.Master.Fields, "__mappingMasterContract", "Master");
            AddContractChecks(lines, definition.Detail.Fields, "__mappingDetailContract", "Detail");
            lines.Add("long? __mappingParentCidContract = __mappingDetailContract." + definition.ParentCidField + ";");
            lines.Add("__mappingDetailContract." + definition.ParentCidField + " = __mappingParentCidContract;");
            lines.Add(
                "var result = MachineDataAcquisitionSystem.Core.Mapping.MappingRuntime.CreateMasterDetail<" +
                definition.Master.TargetModelType + ", " + definition.Detail.TargetModelType +
                ">(\"" + encoded + "\", filePath, machineId);");
            lines.Add("return result;");
            return string.Join(Environment.NewLine, lines);
        }

        private static void AddContractChecks(
            ICollection<string> lines,
            IList<FieldMappingRule> fields,
            string modelVariable,
            string prefix)
        {
            for (int index = 0; index < fields.Count; index++)
            {
                FieldMappingRule field = fields[index];
                string csharpType = ToCSharpType(field.TargetType);
                if (!field.IsRequired && !string.Equals(csharpType, "string", StringComparison.Ordinal))
                    csharpType += "?";
                string variable = "__mapping" + prefix + "Contract" + index;
                lines.Add(csharpType + " " + variable + " = " + modelVariable + "." + field.TargetField + ";");
                lines.Add(modelVariable + "." + field.TargetField + " = " + variable + ";");
            }
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
