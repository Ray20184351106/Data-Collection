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
            new[]
            {
                "cell", "labelOffset", "rowKey", "headerColumn", "rowColumn",
                "fileNameFull", "fileNameStem", "fileNameSegment"
            },
            StringComparer.Ordinal);

        public static readonly ISet<string> AiAllowedLocatorTypes = new HashSet<string>(
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

        internal static string SerializeLegacyDefaultEnumProjection(MappingRuleDefinition rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            JObject token = JObject.Parse(Serialize(rule));
            if (rule.RecordMode == MappingRecordMode.SingleRecord)
            {
                token.Property(nameof(MappingRuleDefinition.RecordMode))?.Remove();
                if (rule.RepeatedRows == null)
                    token.Property(nameof(MappingRuleDefinition.RepeatedRows))?.Remove();
            }
            var fields = token[nameof(MappingRuleDefinition.Fields)] as JArray;
            if (fields != null)
            {
                foreach (JObject field in fields.OfType<JObject>())
                {
                    JProperty scope = field.Property(nameof(FieldMappingRule.Scope));
                    if (scope != null && scope.Value.Type == JTokenType.Integer && scope.Value.Value<int>() == 0)
                        scope.Remove();
                }
            }

            string json = SortToken(token).ToString(Formatting.None);
            ValidateSerializedSize(json);
            return json;
        }

        internal static string SerializePreImageLegacyProjection(MappingRuleDefinition rule)
        {
            JObject token = JObject.Parse(SerializeLegacyDefaultEnumProjection(rule));
            if (rule.ImageArchive == null)
                token.Property(nameof(MappingRuleDefinition.ImageArchive))?.Remove();
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
            if (rule.MasterDetail != null)
            {
                InitializeTarget(rule.MasterDetail.Master);
                InitializeTarget(rule.MasterDetail.Detail);
            }
            return rule;
        }

        private static void InitializeTarget(MappingTargetDefinition target)
        {
            if (target == null) return;
            target.Fields = target.Fields ?? new List<FieldMappingRule>();
            foreach (FieldMappingRule field in target.Fields)
            {
                if (field != null)
                    field.Transforms = field.Transforms ?? new List<string>();
            }
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
            if (normalized != ".xls" && normalized != ".xlsx" &&
                normalized != ".jpg" && normalized != ".jpeg" &&
                normalized != ".png" && normalized != ".bmp")
                throw new MappingValidationException("映射仅支持 .xls、.xlsx、.jpg、.jpeg、.png 和 .bmp。");
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
            if (!Enum.IsDefined(typeof(MappingRecordMode), rule.RecordMode))
                throw new MappingValidationException("记录模式无效。");
            if (rule.RecordMode == MappingRecordMode.ImageFileName)
            {
                ValidateImageFileName(rule);
                return;
            }
            if (rule.ImageArchive != null)
                throw new MappingValidationException("Excel 映射不能包含图片共享目录配置。");
            if (rule.NormalizedExtension != ".xls" && rule.NormalizedExtension != ".xlsx")
                throw new MappingValidationException("Excel 映射仅支持 .xls 和 .xlsx。");
            if (string.IsNullOrWhiteSpace(rule.SheetName))
                throw new MappingValidationException("工作表名称不能为空。");
            ValidateTextLength(rule.SheetName, 128, "工作表名称");
            ValidateTextLength(rule.TemplateSignature, 256, "模板签名");
            if (rule.RecordMode == MappingRecordMode.MasterDetail)
            {
                ValidateMasterDetail(rule);
                return;
            }
            if (rule.MasterDetail != null)
                throw new MappingValidationException("单表规则不能包含主子表配置。");
            ValidateRepeatedRows(rule);
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
                if (!Enum.IsDefined(typeof(MappingFieldScope), field.Scope))
                    throw new MappingValidationException("字段来源范围无效：" + field.TargetField);
                ValidateTextLength(field.TargetDescription, MaximumRuleTextLength, "目标字段说明");
                ValidateTextLength(field.DefaultValue, MaximumRuleTextLength, "默认值");
                ValidateLocator(field.Locator);
                bool isRowLocator = string.Equals(field.Locator.Type, "rowColumn", StringComparison.Ordinal);
                if ((field.Scope == MappingFieldScope.RowColumn) != isRowLocator)
                    throw new MappingValidationException("字段来源范围与定位方式不一致：" + field.TargetField);
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

            if (rule.RecordMode == MappingRecordMode.SingleRecord)
            {
                if (rule.Fields.Any(field => field.Scope != MappingFieldScope.Common))
                    throw new MappingValidationException("单条记录模式不能包含重复行字段。");
            }
            else
            {
                List<FieldMappingRule> rowFields = rule.Fields
                    .Where(field => field.Scope == MappingFieldScope.RowColumn)
                    .ToList();
                if (rowFields.Count == 0)
                    throw new MappingValidationException("重复行模式至少需要一个明细列映射。");
                if (rowFields.Any(field =>
                    field.Locator.ColumnOffset < rule.RepeatedRows.FirstColumnOffset ||
                    field.Locator.ColumnOffset > rule.RepeatedRows.LastColumnOffset))
                    throw new MappingValidationException("明细列映射超出已配置的重复行列范围。");
                if (!rowFields.Any(field => field.Locator != null &&
                    field.Locator.ColumnOffset == rule.RepeatedRows.KeyColumnOffset))
                    throw new MappingValidationException("重复行关键列必须映射到一个目标字段。");
            }
        }

        private static void ValidateImageFileName(MappingRuleDefinition rule)
        {
            if (rule.NormalizedExtension != ".jpg" && rule.NormalizedExtension != ".jpeg" &&
                rule.NormalizedExtension != ".png" && rule.NormalizedExtension != ".bmp")
                throw new MappingValidationException("图片文件名映射仅支持 .jpg、.jpeg、.png 和 .bmp。");
            if (!string.IsNullOrWhiteSpace(rule.SheetName) || rule.RepeatedRows != null || rule.MasterDetail != null)
                throw new MappingValidationException("图片文件名映射不能包含 Excel 工作表、重复行或主子表配置。");
            if (rule.ImageArchive == null)
                throw new MappingValidationException("图片文件名映射缺少共享目录配置。");
            if (string.IsNullOrWhiteSpace(rule.ImageArchive.SharedRootPath) ||
                !Path.IsPathRooted(rule.ImageArchive.SharedRootPath))
                throw new MappingValidationException("图片共享目录必须是绝对路径或 UNC 路径。");
            ValidateTextLength(rule.ImageArchive.SharedRootPath, MaximumRuleTextLength, "图片共享目录");
            if (!IsIdentifier(rule.ImageArchive.PathTargetField))
                throw new MappingValidationException("图片共享路径目标字段无效。");
            if (!string.Equals(rule.ImageArchive.PathTargetType, "string", StringComparison.OrdinalIgnoreCase))
                throw new MappingValidationException("图片共享路径目标字段必须是 string 类型。");
            if (rule.ImageArchive.FileName == null ||
                rule.ImageArchive.FileName.ExpectedSegmentCount < 0 ||
                rule.ImageArchive.FileName.ExpectedSegmentCount > FileNameExtractionParser.MaximumSegments)
                throw new MappingValidationException("图片文件名片段数量超出允许范围。");
            if (rule.Fields != null && rule.Fields.Any(field => field != null &&
                string.Equals(field.TargetField, rule.ImageArchive.PathTargetField, StringComparison.Ordinal)))
                throw new MappingValidationException("图片共享路径字段由系统写入，不能参与文件名映射。");
            ValidateTargetFields(
                rule.Fields,
                null,
                true,
                rule.ImageArchive.FileName.ExpectedSegmentCount,
                "图片");
        }

        private static void ValidateMasterDetail(MappingRuleDefinition rule)
        {
            if (rule.RepeatedRows != null)
                throw new MappingValidationException("主子表规则的重复行配置必须属于子模型。");
            if (rule.Fields != null && rule.Fields.Count > 0)
                throw new MappingValidationException("主子表规则不能使用旧版顶层字段集合。");
            MasterDetailMappingDefinition definition = rule.MasterDetail;
            if (definition == null || definition.Master == null || definition.Detail == null)
                throw new MappingValidationException("主子表规则必须同时配置主模型和子模型。");
            if (definition.FileName == null)
                throw new MappingValidationException("主子表规则缺少文件名解析配置。");
            if (definition.FileName.ExpectedSegmentCount < 0 ||
                definition.FileName.ExpectedSegmentCount > FileNameExtractionParser.MaximumSegments)
                throw new MappingValidationException("文件名片段数量超出允许范围。");
            if (!IsIdentifier(definition.ParentCidField) ||
                string.Equals(definition.ParentCidField, "CID", StringComparison.OrdinalIgnoreCase))
                throw new MappingValidationException("子表关联字段必须是非 CID 的合法标识符。");

            ValidateTargetIdentity(definition.Master, "主模型");
            ValidateTargetIdentity(definition.Detail, "子模型");
            if (definition.Master.ModelId == definition.Detail.ModelId)
                throw new MappingValidationException("主模型和子模型不能是同一个模型。");
            if (definition.Master.ModelId != rule.ModelId ||
                !string.Equals(definition.Master.TargetModelType, rule.TargetModelType, StringComparison.Ordinal) ||
                !string.Equals(definition.Master.ModelSchemaHash, rule.ModelSchemaHash, StringComparison.Ordinal))
                throw new MappingValidationException("主模型配置必须与规则顶层模型身份一致。");
            if (definition.Master.RepeatedRows != null)
                throw new MappingValidationException("主模型不能包含重复行配置。");
            ValidateTargetFields(
                definition.Master.Fields,
                null,
                true,
                definition.FileName.ExpectedSegmentCount,
                "主模型");

            if (definition.Detail.Fields.Any(field =>
                field != null && string.Equals(
                    field.TargetField,
                    definition.ParentCidField,
                    StringComparison.Ordinal)))
                throw new MappingValidationException("子表关联字段由系统写入，不能参与内容映射。");
            var repeatedRule = new MappingRuleDefinition
            {
                RecordMode = MappingRecordMode.RepeatingRows,
                RepeatedRows = definition.Detail.RepeatedRows
            };
            ValidateRepeatedRows(repeatedRule);
            ValidateTargetFields(
                definition.Detail.Fields,
                definition.Detail.RepeatedRows,
                false,
                0,
                "子模型");
        }

        private static void ValidateTargetIdentity(MappingTargetDefinition target, string label)
        {
            if (target.ModelId <= 0)
                throw new MappingValidationException(label + "必须关联现有模型。");
            if (!IsIdentifier(target.TargetModelType))
                throw new MappingValidationException(label + "类型不是安全的 C# 标识符。");
            ValidateTextLength(target.TargetModelType, 128, label + "类型");
            if (string.IsNullOrWhiteSpace(target.ModelSchemaHash))
                throw new MappingValidationException(label + "结构哈希不能为空。");
            ValidateTextLength(target.ModelSchemaHash, 256, label + "结构哈希");
        }

        private static void ValidateTargetFields(
            IList<FieldMappingRule> fields,
            RepeatedRowDefinition repeatedRows,
            bool fileNameOnly,
            int expectedSegmentCount,
            string label)
        {
            if (fields == null || fields.Count == 0)
                throw new MappingValidationException(label + "至少需要一个目标字段映射。");
            if (fields.Count > MaximumMappedFields)
                throw new MappingValidationException(label + "映射字段数量超过上限。");

            var targetNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldMappingRule field in fields)
            {
                if (field == null || !IsIdentifier(field.TargetField))
                    throw new MappingValidationException(label + "目标字段名称无效。");
                if (!targetNames.Add(field.TargetField))
                    throw new MappingValidationException(label + "目标字段不能重复：" + field.TargetField);
                if (!AllowedTargetTypes.Contains((field.TargetType ?? string.Empty).Trim()))
                    throw new MappingValidationException(label + "目标字段类型不受支持：" + field.TargetField);
                if (!Enum.IsDefined(typeof(MappingFieldScope), field.Scope))
                    throw new MappingValidationException(label + "字段来源范围无效：" + field.TargetField);
                ValidateTextLength(field.TargetDescription, MaximumRuleTextLength, "目标字段说明");
                ValidateTextLength(field.DefaultValue, MaximumRuleTextLength, "默认值");
                ValidateLocator(field.Locator);
                string locatorType = field.Locator.Type ?? string.Empty;
                bool isFileName = locatorType == "fileNameFull" ||
                                  locatorType == "fileNameStem" ||
                                  locatorType == "fileNameSegment";
                bool isRow = locatorType == "rowColumn";
                if (fileNameOnly && (!isFileName || field.Scope != MappingFieldScope.Common))
                    throw new MappingValidationException(label + "字段只能来自文件名。");
                if (!fileNameOnly && (isFileName || (field.Scope == MappingFieldScope.RowColumn) != isRow))
                    throw new MappingValidationException("子模型字段来源范围与定位方式不一致：" + field.TargetField);
                if (locatorType == "fileNameSegment" &&
                    (field.Locator.SegmentIndex < 0 || field.Locator.SegmentIndex >= expectedSegmentCount))
                    throw new MappingValidationException("文件名片段索引超出样本范围：" + field.TargetField);
                if (isRow && repeatedRows != null &&
                    (field.Locator.ColumnOffset < repeatedRows.FirstColumnOffset ||
                     field.Locator.ColumnOffset > repeatedRows.LastColumnOffset))
                    throw new MappingValidationException("明细列映射超出已配置的重复行列范围。");

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

            if (!fileNameOnly)
            {
                List<FieldMappingRule> rowFields = fields
                    .Where(field => field.Scope == MappingFieldScope.RowColumn)
                    .ToList();
                if (rowFields.Count == 0)
                    throw new MappingValidationException("子模型至少需要一个重复行字段。");
                if (!rowFields.Any(field =>
                    field.Locator.ColumnOffset == repeatedRows.KeyColumnOffset))
                    throw new MappingValidationException("子模型重复行关键列必须映射到目标字段。");
            }
        }

        private static void ValidateRepeatedRows(MappingRuleDefinition rule)
        {
            if (rule.RecordMode == MappingRecordMode.SingleRecord ||
                rule.RecordMode == MappingRecordMode.ImageFileName)
            {
                if (rule.RepeatedRows != null)
                    throw new MappingValidationException("单条记录模式不能配置重复行区域。");
                return;
            }

            RepeatedRowDefinition rows = rule.RepeatedRows;
            if (rows == null)
                throw new MappingValidationException("重复行模式缺少表格区域配置。");
            if (!Enum.IsDefined(typeof(MappingTableAnchorMode), rows.AnchorMode))
                throw new MappingValidationException("重复行表格锚点模式无效。");
            ValidateTextLength(rows.AnchorText, MaximumRuleTextLength, "重复行锚点文本");
            ValidateTextLength(rows.AnchorCell, 16, "重复行锚点坐标");
            if (rows.AnchorMode == MappingTableAnchorMode.HeaderText &&
                string.IsNullOrWhiteSpace(rows.AnchorText))
                throw new MappingValidationException("表头文本锚点不能为空。");
            if (rows.AnchorMode == MappingTableAnchorMode.FixedCell &&
                !IsCoordinateToken(rows.AnchorCell))
                throw new MappingValidationException("固定表格锚点坐标无效。");
            if (rows.FirstDataRowOffset <= 0 || rows.FirstDataRowOffset > 2000)
                throw new MappingValidationException("首条数据行偏移必须在 1 到 2000 之间。");
            if (rows.KeyColumnOffset < -128 || rows.KeyColumnOffset > 128 ||
                rows.FirstColumnOffset < -128 || rows.FirstColumnOffset > 128 ||
                rows.LastColumnOffset < -128 || rows.LastColumnOffset > 128 ||
                rows.FirstColumnOffset > rows.LastColumnOffset ||
                rows.LastColumnOffset - rows.FirstColumnOffset + 1 > ExcelMappingPreviewService.MaximumColumns ||
                rows.KeyColumnOffset < rows.FirstColumnOffset ||
                rows.KeyColumnOffset > rows.LastColumnOffset)
                throw new MappingValidationException("重复行列范围或关键列偏移无效。");
            if (!rows.StopOnBlankKey)
                throw new MappingValidationException("首版重复行仅支持关键列为空时结束。");
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
            else if (string.Equals(locator.Type, "rowColumn", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(locator.Cell) || !string.IsNullOrEmpty(locator.AnchorCell) ||
                    !string.IsNullOrEmpty(locator.AnchorText) || !string.IsNullOrEmpty(locator.ValueColumn))
                    throw new MappingValidationException("重复行列定位只能包含列偏移和可选表头文本。");
            }
            else if (string.Equals(locator.Type, "fileNameFull", StringComparison.Ordinal) ||
                    string.Equals(locator.Type, "fileNameStem", StringComparison.Ordinal) ||
                    string.Equals(locator.Type, "fileNameSegment", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(locator.Cell) || !string.IsNullOrEmpty(locator.AnchorCell) ||
                    !string.IsNullOrEmpty(locator.AnchorText) || !string.IsNullOrEmpty(locator.Text) ||
                    !string.IsNullOrEmpty(locator.ValueColumn) || locator.RowOffset != 0 ||
                    locator.ColumnOffset != 0 || locator.DataRowOffset != 0)
                    throw new MappingValidationException("文件名定位器包含不允许的工作表参数。");
                if (!string.Equals(locator.Type, "fileNameSegment", StringComparison.Ordinal) &&
                    locator.SegmentIndex != 0)
                    throw new MappingValidationException("完整文件名定位器不能配置片段索引。");
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
