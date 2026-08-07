using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public static class MappingRuleSerializer
    {
        public const int MaximumSerializedRuleBytes = 1024 * 1024;
        public const int MaximumMappedFields = 512;
        public const int MaximumRuleTextLength = 2048;

        private static readonly ISet<string> CSharpKeywords = new HashSet<string>(new[]
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        }, StringComparer.Ordinal);

        public static readonly ISet<string> AllowedLocatorTypes = new HashSet<string>(
            new[] { "cell", "labelOffset", "rowKey", "headerColumn" },
            StringComparer.Ordinal);

        public static readonly ISet<string> AllowedTransforms = new HashSet<string>(
            new[] { "trim", "normalizeWhitespace", "integer", "decimal", "date", "valueMap", "default" },
            StringComparer.Ordinal);

        public static readonly ISet<string> AllowedTargetTypes = new HashSet<string>(
            new[] { "string", "int", "long", "decimal", "float", "double", "datetime", "bool" },
            StringComparer.OrdinalIgnoreCase);

        public static string Serialize(MappingRuleDefinition rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            JToken token = JToken.FromObject(rule, JsonSerializer.Create(new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
                Culture = CultureInfo.InvariantCulture
            }));
            string json = SortToken(token).ToString(Formatting.None);
            ValidateSerializedSize(json);
            return json;
        }

        public static MappingRuleDefinition Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new MappingValidationException("映射规则 JSON 不能为空。");
            ValidateSerializedSize(json);

            var rule = JsonConvert.DeserializeObject<MappingRuleDefinition>(json);
            if (rule == null)
                throw new MappingValidationException("映射规则 JSON 无效。");
            rule.Fields = rule.Fields ?? new List<FieldMappingRule>();
            foreach (FieldMappingRule field in rule.Fields)
            {
                if (field != null)
                    field.Transforms = field.Transforms ?? new List<string>();
            }
            return rule;
        }

        public static string Sha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static string FileSha256(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static string NormalizeExtension(string extension)
        {
            string normalized = (extension ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length == 0)
                throw new MappingValidationException("文件扩展名不能为空。");
            if (!normalized.StartsWith(".", StringComparison.Ordinal))
                normalized = "." + normalized;
            if (normalized != ".xls" && normalized != ".xlsx")
                throw new MappingValidationException("首版映射仅支持 .xls 和 .xlsx。");
            return normalized;
        }

        public static void ValidateDefinition(MappingRuleDefinition rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (string.IsNullOrWhiteSpace(rule.RuleName))
                throw new MappingValidationException("规则名称不能为空。");
            ValidateTextLength(rule.RuleName, 256, "规则名称");
            if (rule.ModelId <= 0)
                throw new MappingValidationException("必须关联现有模型。");
            if (!IsIdentifier(rule.TargetModelType))
                throw new MappingValidationException("目标模型类型不是安全的 C# 类型标识符。");
            ValidateTextLength(rule.TargetModelType, 128, "目标模型类型");
            if (string.IsNullOrWhiteSpace(rule.ModelSchemaHash))
                throw new MappingValidationException("模型结构哈希不能为空。");
            ValidateTextLength(rule.ModelSchemaHash, 256, "模型结构哈希");
            rule.NormalizedExtension = NormalizeExtension(rule.NormalizedExtension);
            if (string.IsNullOrWhiteSpace(rule.SheetName))
                throw new MappingValidationException("工作表名称不能为空。");
            ValidateTextLength(rule.SheetName, 128, "工作表名称");
            ValidateTextLength(rule.TemplateSignature, 256, "模板签名");
            if (rule.Fields == null || rule.Fields.Count == 0)
                throw new MappingValidationException("映射至少需要一个目标字段。");
            if (rule.Fields.Count > MaximumMappedFields)
                throw new MappingValidationException("映射字段数量超过上限。");

            var targetNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldMappingRule field in rule.Fields)
            {
                if (field == null || !IsIdentifier(field.TargetField))
                    throw new MappingValidationException("目标字段名称无效。");
                ValidateTextLength(field.TargetField, 128, "目标字段名称");
                if (!targetNames.Add(field.TargetField))
                    throw new MappingValidationException("目标字段不能重复：" + field.TargetField);
                if (!AllowedTargetTypes.Contains((field.TargetType ?? string.Empty).Trim()))
                    throw new MappingValidationException("目标字段类型不受支持：" + field.TargetField);
                ValidateTextLength(field.TargetDescription, MaximumRuleTextLength, "目标字段说明");
                ValidateTextLength(field.DefaultValue, MaximumRuleTextLength, "默认值");
                ValidateLocator(field.Locator);
                var transforms = field.Transforms ?? new List<string>();
                if (transforms.Count > AllowedTransforms.Count ||
                    transforms.Distinct(StringComparer.Ordinal).Count() != transforms.Count)
                    throw new MappingValidationException("转换器不能重复且数量不能超过白名单范围。");
                foreach (string transform in transforms)
                {
                    if (!AllowedTransforms.Contains(transform))
                        throw new MappingValidationException("不允许的转换器：" + transform);
                }
                ValidateTransformParameters(field);
            }
        }

        private static void ValidateTransformParameters(FieldMappingRule field)
        {
            bool usesValueMap = (field.Transforms ?? new List<string>()).Contains("valueMap");
            int mapCount = field.ExactValueMap == null ? 0 : field.ExactValueMap.Count;
            if (usesValueMap != (mapCount > 0))
                throw new MappingValidationException("valueMap 转换必须同时提供精确值映射：" + field.TargetField);
            if (mapCount > 128)
                throw new MappingValidationException("单个字段最多允许 128 个精确值映射。");
            foreach (KeyValuePair<string, string> item in field.ExactValueMap ??
                new Dictionary<string, string>(StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(item.Key) || item.Key.Length > 256 ||
                    (item.Value ?? string.Empty).Length > 256 ||
                    item.Key.Any(char.IsControl) || (item.Value ?? string.Empty).Any(char.IsControl))
                    throw new MappingValidationException("精确值映射包含无效的源值或目标值。");
            }
            if ((field.Transforms ?? new List<string>()).Contains("default") && field.DefaultValue == null)
                throw new MappingValidationException("default 转换必须提供默认值：" + field.TargetField);
            if (field.IsRequired &&
                ((field.Transforms ?? new List<string>()).Contains("default") ||
                 !string.IsNullOrEmpty(field.DefaultValue)))
                throw new MappingValidationException("必填字段不能使用默认值：" + field.TargetField);
        }

        public static void ValidateLocator(MappingLocator locator)
        {
            if (locator == null || !AllowedLocatorTypes.Contains(locator.Type ?? string.Empty))
                throw new MappingValidationException("定位器类型不在白名单内。");
            if (locator.RowOffset < -2000 || locator.RowOffset > 2000 ||
                locator.ColumnOffset < -128 || locator.ColumnOffset > 128 ||
                locator.DataRowOffset < -2000 || locator.DataRowOffset > 2000)
                throw new MappingValidationException("定位器偏移超出允许范围。");

            ValidateTextLength(locator.Cell, 16, "固定单元格坐标");
            ValidateTextLength(locator.AnchorCell, 16, "结构锚点坐标");
            ValidateTextLength(locator.AnchorText, MaximumRuleTextLength, "结构锚点文本");
            ValidateTextLength(locator.Text, MaximumRuleTextLength, "定位文本");
            ValidateTextLength(locator.ValueColumn, 3, "目标列");

            if (string.Equals(locator.Type, "cell", StringComparison.Ordinal))
            {
                if (!IsCoordinateToken(locator.Cell))
                    throw new MappingValidationException("固定单元格坐标无效。");
                if (!IsCoordinateToken(locator.AnchorCell) || string.IsNullOrWhiteSpace(locator.AnchorText))
                    throw new MappingValidationException("固定单元格映射必须包含结构锚点坐标和文本。");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(locator.Text))
                    throw new MappingValidationException("标签、行键或表头定位必须包含定位文本。");
                if (string.Equals(locator.Type, "rowKey", StringComparison.Ordinal) &&
                    !IsColumnToken(locator.ValueColumn))
                    throw new MappingValidationException("行键定位的目标列无效。");
            }
        }

        public static void ValidateSerializedSize(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (Encoding.UTF8.GetByteCount(json) > MaximumSerializedRuleBytes)
                throw new MappingValidationException("映射规则超过 1 MiB 大小上限。");
        }

        public static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (CSharpKeywords.Contains(value)) return false;
            if (!(char.IsLetter(value[0]) || value[0] == '_')) return false;
            for (int i = 1; i < value.Length; i++)
            {
                if (!(char.IsLetterOrDigit(value[i]) || value[i] == '_')) return false;
            }
            return true;
        }

        private static void ValidateTextLength(string value, int maximumLength, string fieldName)
        {
            if (value != null && value.Length > maximumLength)
                throw new MappingValidationException(fieldName + "超过长度上限。");
        }

        private static bool IsCoordinateToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string token = value.Trim();
            int index = 0;
            while (index < token.Length &&
                   ((token[index] >= 'A' && token[index] <= 'Z') ||
                    (token[index] >= 'a' && token[index] <= 'z')))
                index++;
            if (index == 0 || index > 3 || index == token.Length) return false;
            for (; index < token.Length; index++)
            {
                if (token[index] < '0' || token[index] > '9') return false;
            }
            return true;
        }

        private static bool IsColumnToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string token = value.Trim();
            if (token.Length > 3) return false;
            return token.All(character =>
                (character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z'));
        }

        private static JToken SortToken(JToken token)
        {
            var obj = token as JObject;
            if (obj != null)
            {
                var sorted = new JObject();
                foreach (JProperty property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    sorted.Add(property.Name, SortToken(property.Value));
                return sorted;
            }

            var array = token as JArray;
            if (array != null)
                return new JArray(array.Select(SortToken));
            return token.DeepClone();
        }
    }
}
