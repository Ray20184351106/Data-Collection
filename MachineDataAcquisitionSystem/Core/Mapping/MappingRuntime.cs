using System;
using System.Collections;
using System.Collections.Generic;
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
            MappingRuleDefinition rule = DecodeRule(encodedRule);
            MappingRuleSerializer.ValidateDefinition(rule);
            if (rule.RecordMode != MappingRecordMode.SingleRecord)
                throw new MappingValidationException("重复行规则必须使用批量模型运行时。");
            Type modelType = model.GetType();
            EnsureModelType(modelType, rule.TargetModelType);

            MappingPreviewResult preview = new ExcelMappingPreviewService().Preview(filePath, rule);
            if (!preview.IsValid)
                throw new MappingValidationException(BuildPreviewErrorMessage(preview));

            PopulateProperties(model, modelType, rule.Fields, preview.Fields);
        }

        public static void PopulateImageModel(object model, string encodedRule, string filePath, int machineId)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            MappingRuleDefinition rule = DecodeRule(encodedRule);
            MappingRuleSerializer.ValidateDefinition(rule);
            if (rule.RecordMode != MappingRecordMode.ImageFileName)
                throw new MappingValidationException("非图片文件名规则不能使用图片映射运行时。");
            Type modelType = model.GetType();
            EnsureModelType(modelType, rule.TargetModelType);

            MappingPreviewResult preview = new ImageFileMappingService().Preview(filePath, rule);
            if (!preview.IsValid)
                throw new MappingValidationException(BuildPreviewErrorMessage(preview));
            PopulateProperties(model, modelType, rule.Fields, preview.Fields);
        }

        public static List<T> CreateModels<T>(string encodedRule, string filePath, int machineId)
            where T : new()
        {
            MappingRuleDefinition rule = DecodeRule(encodedRule);
            MappingRuleSerializer.ValidateDefinition(rule);
            if (rule.RecordMode != MappingRecordMode.RepeatingRows)
                throw new MappingValidationException("单条记录规则不能使用批量模型运行时。");

            Type modelType = typeof(T);
            EnsureModelType(modelType, rule.TargetModelType);
            MappingPreviewResult preview = new ExcelMappingPreviewService().Preview(filePath, rule);
            if (!preview.IsValid)
                throw new MappingValidationException(BuildPreviewErrorMessage(preview));

            var models = new List<T>(preview.Records.Count);
            foreach (MappingPreviewRecordResult record in preview.Records)
            {
                var model = new T();
                PopulateProperties(model, modelType, rule.Fields, record.Fields);
                models.Add(model);
            }
            return models;
        }

        public static MasterDetailParseResult CreateMasterDetail<TMaster, TDetail>(
            string encodedRule,
            string filePath,
            int machineId)
            where TMaster : new()
            where TDetail : new()
        {
            MappingRuleDefinition rule = DecodeRule(encodedRule);
            MappingRuleSerializer.ValidateDefinition(rule);
            if (rule.RecordMode != MappingRecordMode.MasterDetail)
                throw new MappingValidationException("非主子表规则不能使用主子表运行时。");

            MasterDetailMappingDefinition definition = rule.MasterDetail;
            EnsureModelType(typeof(TMaster), definition.Master.TargetModelType);
            EnsureModelType(typeof(TDetail), definition.Detail.TargetModelType);
            PropertyInfo parentCid = typeof(TDetail).GetProperty(
                definition.ParentCidField,
                BindingFlags.Instance | BindingFlags.Public);
            Type parentCidType = parentCid == null
                ? null
                : Nullable.GetUnderlyingType(parentCid.PropertyType) ?? parentCid.PropertyType;
            if (parentCid == null || !parentCid.CanWrite || parentCidType != typeof(long))
                throw new MappingValidationException("子模型缺少可写的 long 类型关联字段：" + definition.ParentCidField);

            MappingPreviewResult preview = new ExcelMappingPreviewService().Preview(filePath, rule);
            if (!preview.IsValid)
                throw new MappingValidationException(BuildPreviewErrorMessage(preview));
            if (preview.Records.Count == 0)
                throw new MappingValidationException("主子表规则没有解析到任何子表记录。");

            var master = new TMaster();
            PopulateProperties(master, typeof(TMaster), definition.Master.Fields, preview.Fields);
            var details = new List<object>(preview.Records.Count);
            foreach (MappingPreviewRecordResult record in preview.Records)
            {
                var detail = new TDetail();
                PopulateProperties(detail, typeof(TDetail), definition.Detail.Fields, record.Fields);
                details.Add(detail);
            }

            return new MasterDetailParseResult(
                master,
                details,
                definition.Master.ModelId,
                definition.Master.TargetModelType,
                definition.Detail.ModelId,
                definition.Detail.TargetModelType,
                definition.ParentCidField);
        }

        private static string BuildPreviewErrorMessage(MappingPreviewResult preview)
        {
            var details = new List<string>();
            foreach (MappingPreviewRecordResult record in preview.Records)
            {
                foreach (MappingPreviewFieldResult field in record.Fields.Values.Where(
                    item => !string.IsNullOrWhiteSpace(item.ErrorCode)))
                {
                    details.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "Excel第{0}行 {1} 字段{2}: {3}",
                        record.ExcelRowNumber,
                        field.SourceCell ?? "?",
                        field.TargetField ?? "?",
                        field.ErrorCode));
                }
            }
            foreach (MappingPreviewFieldResult field in preview.Fields.Values.Where(
                item => !string.IsNullOrWhiteSpace(item.ErrorCode)))
            {
                details.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "公共字段 {0} ({1}): {2}",
                    field.TargetField ?? "?",
                    field.SourceCell ?? "?",
                    field.ErrorCode));
            }
            if (details.Count == 0)
                details.AddRange(preview.ErrorCodes);
            return "模板或映射验证失败：" + string.Join("; ", details);
        }

        private static MappingRuleDefinition DecodeRule(string encodedRule)
        {
            if (string.IsNullOrWhiteSpace(encodedRule))
                throw new MappingValidationException("映射规则常量不能为空。");
            try
            {
                byte[] bytes = Convert.FromBase64String(encodedRule);
                if (bytes.Length > MappingRuleSerializer.MaximumSerializedRuleBytes)
                    throw new MappingValidationException("映射规则超过运行时大小上限。");
                return MappingRuleSerializer.Deserialize(Encoding.UTF8.GetString(bytes));
            }
            catch (FormatException ex)
            {
                throw new MappingValidationException("映射规则常量不是有效的 Base64：" + ex.Message);
            }
        }

        private static void EnsureModelType(Type modelType, string expectedType)
        {
            if (!string.Equals(modelType.Name, expectedType, StringComparison.Ordinal) &&
                !string.Equals(modelType.FullName, expectedType, StringComparison.Ordinal))
                throw new MappingValidationException("运行时模型与规则关联模型不一致。");
        }

        private static void PopulateProperties(
            object model,
            Type modelType,
            IEnumerable<FieldMappingRule> fields,
            IDictionary<string, MappingPreviewFieldResult> values)
        {
            foreach (FieldMappingRule field in fields)
            {
                PropertyInfo property = modelType.GetProperty(field.TargetField, BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanWrite)
                    throw new MappingValidationException("模型中不存在可写字段：" + field.TargetField);
                MappingPreviewFieldResult previewField;
                if (!values.TryGetValue(field.TargetField, out previewField))
                    throw new MappingValidationException("预览结果缺少目标字段：" + field.TargetField);
                property.SetValue(model, ConvertValue(previewField.Value, property.PropertyType), null);
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

    public static class MappingResultNormalizer
    {
        public const int MaximumRecordsPerFile = ExcelMappingPreviewService.MaximumRows;

        public static IReadOnlyList<object> Normalize(object result, string targetModelType)
        {
            if (result == null)
                throw new MappingValidationException("解析规则返回了空结果。");

            var records = new List<object>();
            IEnumerable enumerable = result as IEnumerable;
            if (enumerable != null && !(result is string) && !(result is byte[]))
            {
                foreach (object item in enumerable)
                {
                    if (item == null)
                        throw new MappingValidationException("解析结果集合中包含空记录。");
                    records.Add(item);
                    if (records.Count > MaximumRecordsPerFile)
                        throw new MappingValidationException("单个文件解析记录数超过上限。");
                }
            }
            else
            {
                records.Add(result);
            }

            if (records.Count == 0)
                throw new MappingValidationException("解析规则没有返回任何记录。");
            Type firstType = records[0].GetType();
            foreach (object record in records)
            {
                Type type = record.GetType();
                if (type != firstType)
                    throw new MappingValidationException("解析结果集合中包含不同的模型类型。");
                if (!string.IsNullOrWhiteSpace(targetModelType) &&
                    !string.Equals(type.Name, targetModelType, StringComparison.Ordinal) &&
                    !string.Equals(type.FullName, targetModelType, StringComparison.Ordinal))
                    throw new MappingValidationException("解析结果类型与规则关联模型不一致。");
            }
            return records;
        }

        public static MasterDetailParseResult NormalizeMasterDetail(
            object result,
            string masterModelType,
            string detailModelType)
        {
            var aggregate = result as MasterDetailParseResult;
            if (aggregate == null)
                throw new MappingValidationException("主子表规则返回了无效结果类型。");
            if (aggregate.Master == null)
                throw new MappingValidationException("主子表规则没有返回主表记录。");
            EnsureType(aggregate.Master, masterModelType, "主表");
            if (aggregate.Details == null || aggregate.Details.Count == 0)
                throw new MappingValidationException("主子表规则没有返回任何子表记录。");
            if (aggregate.Details.Count > MaximumRecordsPerFile)
                throw new MappingValidationException("单个文件解析记录数超过上限。");
            foreach (object detail in aggregate.Details)
            {
                if (detail == null)
                    throw new MappingValidationException("主子表结果包含空的子表记录。");
                EnsureType(detail, detailModelType, "子表");
            }
            return aggregate;
        }

        private static void EnsureType(object value, string expectedType, string label)
        {
            Type type = value.GetType();
            if (!string.Equals(type.Name, expectedType, StringComparison.Ordinal) &&
                !string.Equals(type.FullName, expectedType, StringComparison.Ordinal))
                throw new MappingValidationException(label + "结果类型与规则关联模型不一致。");
        }
    }

    public sealed class MasterDetailParseResult
    {
        public MasterDetailParseResult(
            object master,
            IEnumerable<object> details,
            int masterModelId,
            string masterModelType,
            int detailModelId,
            string detailModelType,
            string parentCidField)
        {
            Master = master;
            Details = (details ?? Enumerable.Empty<object>()).ToList().AsReadOnly();
            MasterModelId = masterModelId;
            MasterModelType = masterModelType;
            DetailModelId = detailModelId;
            DetailModelType = detailModelType;
            ParentCidField = parentCidField;
        }

        public object Master { get; private set; }
        public IReadOnlyList<object> Details { get; private set; }
        public int MasterModelId { get; private set; }
        public string MasterModelType { get; private set; }
        public int DetailModelId { get; private set; }
        public string DetailModelType { get; private set; }
        public string ParentCidField { get; private set; }
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
