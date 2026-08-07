using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public static class MappingRuntime
    {
        public static void PopulateModel(object model, string encodedRule, string filePath, int machineId)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(encodedRule))
                throw new MappingValidationException("映射规则常量不能为空。");

            string json;
            try
            {
                byte[] bytes = Convert.FromBase64String(encodedRule);
                if (bytes.Length > MappingRuleSerializer.MaximumSerializedRuleBytes)
                    throw new MappingValidationException("映射规则超过运行时大小上限。");
                json = Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException ex)
            {
                throw new MappingValidationException("映射规则常量不是有效的 Base64：" + ex.Message);
            }

            MappingRuleDefinition rule = MappingRuleSerializer.Deserialize(json);
            MappingRuleSerializer.ValidateDefinition(rule);
            Type modelType = model.GetType();
            if (!string.Equals(modelType.Name, rule.TargetModelType, StringComparison.Ordinal) &&
                !string.Equals(modelType.FullName, rule.TargetModelType, StringComparison.Ordinal))
                throw new MappingValidationException("运行时模型与规则关联模型不一致。");

            MappingPreviewResult preview = new ExcelMappingPreviewService().Preview(filePath, rule);
            if (!preview.IsValid)
                throw new MappingValidationException("模板或映射验证失败：" + string.Join(",", preview.ErrorCodes));

            foreach (FieldMappingRule field in rule.Fields)
            {
                PropertyInfo property = modelType.GetProperty(field.TargetField, BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanWrite)
                    throw new MappingValidationException("模型中不存在可写字段：" + field.TargetField);

                object value = preview.Fields[field.TargetField].Value;
                property.SetValue(model, ConvertValue(value, property.PropertyType), null);
            }
        }

        private static object ConvertValue(object value, Type targetType)
        {
            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value == null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    return Activator.CreateInstance(targetType);
                return null;
            }
            if (underlying.IsInstanceOfType(value)) return value;
            if (underlying.IsEnum)
                return Enum.Parse(underlying, Convert.ToString(value, CultureInfo.InvariantCulture), true);
            if (underlying == typeof(Guid))
                return Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
            if (underlying == typeof(DateTime))
                return value is DateTime
                    ? value
                    : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }
    }

    public sealed class LocalMappingAssistant
    {
        public System.Collections.Generic.IReadOnlyList<AiMappingSuggestion> Suggest(
            MappingWorkbookSnapshot snapshot,
            System.Collections.Generic.IEnumerable<AiTargetField> targets)
        {
            var suggestions = new System.Collections.Generic.List<AiMappingSuggestion>();
            if (snapshot == null) return suggestions;

            var cells = snapshot.Sheets.SelectMany(sheet => sheet.Cells.Select(cell => new { sheet, cell })).ToList();
            foreach (AiTargetField target in targets ?? Enumerable.Empty<AiTargetField>())
            {
                string[] candidates = new[] { target.FieldName, target.Description }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var matches = cells.Where(item => candidates.Any(candidate =>
                    string.Equals(Normalize(candidate), Normalize(item.cell.DisplayText), StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (matches.Count != 1) continue;

                suggestions.Add(new AiMappingSuggestion
                {
                    TargetField = target.FieldName,
                    Locator = new MappingLocator
                    {
                        Type = "labelOffset",
                        Text = matches[0].cell.DisplayText,
                        ColumnOffset = 1
                    },
                    Confidence = 1m,
                    Reason = "字段名或说明与样本标签精确匹配。"
                });
            }
            return suggestions;
        }

        private static string Normalize(string value)
        {
            return string.Concat((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
        }
    }
}
