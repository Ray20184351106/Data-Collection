using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using MachineDataAcquisitionSystem.Core.Mapping;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace MachineDataAcquisitionSystem.Core
{
    public sealed class ModelExcelImportService
    {
        public const long MaximumFileBytes = 5L * 1024L * 1024L;
        public const int MaximumModels = 1000;
        public const int MaximumFields = 20000;

        private const int MaximumNameLength = 256;
        private const int MaximumDescriptionLength = 2048;
        private const string SheetName = "数据模型";

        private static readonly string[] RequiredHeaders =
        {
            "模型名称", "表名", "模型说明", "父模型", "启用", "字段名称",
            "字段类型", "字段长度", "必填", "主键", "自增", "字段说明"
        };

        private readonly string _connectionString;

        public ModelExcelImportService(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("数据库连接字符串不能为空。", nameof(connectionString));
            _connectionString = connectionString;
        }

        public ModelExcelImportPreview Preview(string filePath)
        {
            var models = new List<ModelExcelImportModel>();
            var errors = new List<ModelExcelImportError>();
            if (!ValidateInputFile(filePath, errors))
                return new ModelExcelImportPreview(filePath, models, errors);

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (IWorkbook workbook = WorkbookFactory.Create(stream))
                {
                    ISheet sheet = workbook.GetSheet(SheetName);
                    if (sheet == null)
                    {
                        errors.Add(new ModelExcelImportError(0, "工作表", "未找到名为“数据模型”的工作表。"));
                        return new ModelExcelImportPreview(filePath, models, errors);
                    }

                    ParseSheet(sheet, models, errors);
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ModelExcelImportError(0, "文件", "无法读取 Excel 文件：" + ex.Message));
                return new ModelExcelImportPreview(filePath, models, errors);
            }

            ValidateWorkbookRelationships(models, errors);
            if (models.Count == 0 && errors.Count == 0)
                errors.Add(new ModelExcelImportError(0, "数据", "工作表中没有可导入的数据行。"));

            if (models.Count > 0)
            {
                try
                {
                    using (var connection = new SQLiteConnection(_connectionString))
                    {
                        connection.Open();
                        ValidateAgainstDatabase(models, connection, null, errors);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new ModelExcelImportError(0, "数据库", "无法校验现有数据模型：" + ex.Message));
                }
            }

            return new ModelExcelImportPreview(filePath, models, errors);
        }

        public ModelExcelImportResult Import(ModelExcelImportPreview preview)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            if (!preview.CanImport)
                throw new ModelExcelImportValidationException(preview.Errors);

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    var currentErrors = new List<ModelExcelImportError>();
                    ValidateAgainstDatabase(preview.Models, connection, transaction, currentErrors);
                    if (currentErrors.Count > 0)
                        throw new ModelExcelImportValidationException(currentErrors);

                    Dictionary<string, int> existingIds = LoadExistingModelIds(connection, transaction);
                    var importedIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    var importedModels = new List<ModelExcelImportedModel>();

                    foreach (ModelExcelImportModel model in preview.Models)
                    {
                        int temporaryParentId = ResolveExistingParentId(model.ParentModelName, existingIds);
                        int modelId = InsertModel(connection, transaction, model, temporaryParentId);
                        importedIds.Add(model.ModelName, modelId);
                        importedModels.Add(new ModelExcelImportedModel(modelId, model));

                        foreach (ModelExcelImportField field in model.Fields)
                            InsertField(connection, transaction, modelId, field);
                    }

                    foreach (ModelExcelImportModel model in preview.Models)
                    {
                        int parentId = ResolveParentId(model.ParentModelName, existingIds, importedIds);
                        UpdateParentId(connection, transaction, importedIds[model.ModelName], parentId);
                    }

                    transaction.Commit();
                    return new ModelExcelImportResult(importedModels);
                }
            }
        }

        public void CreateTemplate(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("模板保存路径不能为空。", nameof(filePath));
            if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("模板必须保存为 .xlsx 文件。", nameof(filePath));

            using (IWorkbook workbook = new XSSFWorkbook())
            {
                ISheet sheet = workbook.CreateSheet(SheetName);
                IRow header = sheet.CreateRow(0);
                ICellStyle headerStyle = workbook.CreateCellStyle();
                headerStyle.FillForegroundColor = IndexedColors.Grey25Percent.Index;
                headerStyle.FillPattern = FillPattern.SolidForeground;
                headerStyle.Alignment = HorizontalAlignment.Center;
                IFont headerFont = workbook.CreateFont();
                headerFont.IsBold = true;
                headerStyle.SetFont(headerFont);

                for (int column = 0; column < RequiredHeaders.Length; column++)
                {
                    ICell cell = header.CreateCell(column);
                    cell.SetCellValue(RequiredHeaders[column]);
                    cell.CellStyle = headerStyle;
                    sheet.SetColumnWidth(column, (column == 2 || column == 11 ? 24 : 16) * 256);
                }
                sheet.CreateFreezePane(0, 1);

                ISheet instructions = workbook.CreateSheet("填写说明");
                string[] lines =
                {
                    "填写规则",
                    "1. “数据模型”工作表中每行代表一个字段，同一模型的模型信息需要在每行重复填写。",
                    "2. 模型名称、表名、字段名称、字段类型为必填项。",
                    "3. 字段类型：string、int、long、decimal、float、double、datetime、bool。",
                    "4. 布尔值可填写：是/否、true/false、1/0；启用留空时默认停用。",
                    "5. 父模型留空表示无，填写 BaseEntity 表示系统基类，也可填写已有或同批模型名称。",
                    "6. 导入只新增模型；任何错误或冲突都会阻止整批导入，不会覆盖已有模型。"
                };
                for (int row = 0; row < lines.Length; row++)
                    instructions.CreateRow(row).CreateCell(0).SetCellValue(lines[row]);
                instructions.SetColumnWidth(0, 100 * 256);

                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    workbook.Write(stream);
            }
        }

        private static bool ValidateInputFile(string filePath, ICollection<ModelExcelImportError> errors)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                errors.Add(new ModelExcelImportError(0, "文件", "Excel 文件不存在。"));
                return false;
            }

            string extension = Path.GetExtension(filePath);
            if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ModelExcelImportError(0, "文件", "只允许导入 .xlsx 或 .xls 文件。"));
                return false;
            }

            if (new FileInfo(filePath).Length > MaximumFileBytes)
            {
                errors.Add(new ModelExcelImportError(0, "文件", "Excel 文件不能超过 5 MB。"));
                return false;
            }
            return true;
        }

        private static void ParseSheet(
            ISheet sheet,
            IList<ModelExcelImportModel> models,
            IList<ModelExcelImportError> errors)
        {
            Dictionary<string, int> columns = ReadHeaders(sheet.GetRow(0), errors);
            if (errors.Count > 0) return;

            var modelsByName = new Dictionary<string, ModelExcelImportModel>(StringComparer.OrdinalIgnoreCase);
            int fieldCount = 0;
            for (int rowIndex = 1; rowIndex <= sheet.LastRowNum; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                if (row == null || IsEmptyRow(row, columns)) continue;

                fieldCount++;
                if (fieldCount > MaximumFields)
                {
                    errors.Add(new ModelExcelImportError(rowIndex + 1, "数据", "字段总数不能超过 20,000。"));
                    break;
                }

                string modelName = Read(row, columns, "模型名称");
                string tableName = Read(row, columns, "表名");
                string modelDescription = Read(row, columns, "模型说明");
                string parentName = Read(row, columns, "父模型");
                string fieldName = Read(row, columns, "字段名称");
                string fieldType = Read(row, columns, "字段类型").ToLowerInvariant();
                string fieldDescription = Read(row, columns, "字段说明");

                ValidateRequired(modelName, rowIndex, "模型名称", errors);
                ValidateRequired(tableName, rowIndex, "表名", errors);
                ValidateRequired(fieldName, rowIndex, "字段名称", errors);
                ValidateRequired(fieldType, rowIndex, "字段类型", errors);
                ValidateLength(modelName, MaximumNameLength, rowIndex, "模型名称", errors);
                ValidateLength(tableName, MaximumNameLength, rowIndex, "表名", errors);
                ValidateLength(parentName, MaximumNameLength, rowIndex, "父模型", errors);
                ValidateLength(fieldName, MaximumNameLength, rowIndex, "字段名称", errors);
                ValidateLength(modelDescription, MaximumDescriptionLength, rowIndex, "模型说明", errors);
                ValidateLength(fieldDescription, MaximumDescriptionLength, rowIndex, "字段说明", errors);

                if (!string.IsNullOrWhiteSpace(modelName) && !MappingRuleSerializer.IsIdentifier(modelName))
                    errors.Add(new ModelExcelImportError(rowIndex + 1, "模型名称", "必须是合法的 C# 标识符。"));
                if (!string.IsNullOrWhiteSpace(fieldName) && !MappingRuleSerializer.IsIdentifier(fieldName))
                    errors.Add(new ModelExcelImportError(rowIndex + 1, "字段名称", "必须是合法的 C# 标识符。"));
                if (!string.IsNullOrWhiteSpace(fieldType) && !MappingRuleSerializer.AllowedTargetTypes.Contains(fieldType))
                    errors.Add(new ModelExcelImportError(rowIndex + 1, "字段类型", "不支持类型“" + fieldType + "”。"));

                int fieldLength = ParseNonNegativeInteger(Read(row, columns, "字段长度"), rowIndex, "字段长度", errors);
                bool isActive = ParseBoolean(Read(row, columns, "启用"), rowIndex, "启用", errors);
                bool isRequired = ParseBoolean(Read(row, columns, "必填"), rowIndex, "必填", errors);
                bool isPrimaryKey = ParseBoolean(Read(row, columns, "主键"), rowIndex, "主键", errors);
                bool isIdentity = ParseBoolean(Read(row, columns, "自增"), rowIndex, "自增", errors);

                if (string.IsNullOrWhiteSpace(modelName)) continue;
                if (!modelsByName.TryGetValue(modelName, out ModelExcelImportModel model))
                {
                    if (models.Count >= MaximumModels)
                    {
                        errors.Add(new ModelExcelImportError(rowIndex + 1, "模型名称", "模型总数不能超过 1,000。"));
                        continue;
                    }
                    model = new ModelExcelImportModel(
                        rowIndex + 1,
                        modelName,
                        tableName,
                        modelDescription,
                        parentName,
                        isActive);
                    modelsByName.Add(modelName, model);
                    models.Add(model);
                }
                else
                {
                    ValidateConsistent(model.TableName, tableName, rowIndex, "表名", errors);
                    ValidateConsistent(model.Description, modelDescription, rowIndex, "模型说明", errors);
                    ValidateConsistent(model.ParentModelName, parentName, rowIndex, "父模型", errors);
                    if (model.IsActive != isActive)
                        errors.Add(new ModelExcelImportError(rowIndex + 1, "启用", "同一模型的启用值不一致。"));
                }

                if (string.IsNullOrWhiteSpace(fieldName)) continue;
                if (model.MutableFields.Any(field => string.Equals(field.FieldName, fieldName, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add(new ModelExcelImportError(rowIndex + 1, "字段名称", "同一模型中字段“" + fieldName + "”重复。"));
                    continue;
                }

                model.MutableFields.Add(new ModelExcelImportField(
                    rowIndex + 1,
                    fieldName,
                    fieldType,
                    fieldLength,
                    isRequired,
                    isPrimaryKey,
                    isIdentity,
                    fieldDescription));
            }
        }

        private static Dictionary<string, int> ReadHeaders(IRow row, IList<ModelExcelImportError> errors)
        {
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            if (row != null)
            {
                for (int column = 0; column < row.LastCellNum; column++)
                {
                    string name = ReadCell(row.GetCell(column));
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (columns.ContainsKey(name))
                        errors.Add(new ModelExcelImportError(1, name, "表头重复。"));
                    else
                        columns.Add(name, column);
                }
            }

            foreach (string header in RequiredHeaders)
            {
                if (!columns.ContainsKey(header))
                    errors.Add(new ModelExcelImportError(1, header, "缺少必需表头。"));
            }
            return columns;
        }

        private static bool IsEmptyRow(IRow row, IDictionary<string, int> columns)
        {
            return RequiredHeaders.All(header => string.IsNullOrWhiteSpace(Read(row, columns, header)));
        }

        private static string Read(IRow row, IDictionary<string, int> columns, string header)
        {
            return ReadCell(row.GetCell(columns[header])).Trim();
        }

        private static string ReadCell(ICell cell)
        {
            if (cell == null) return string.Empty;
            CellType type = cell.CellType == CellType.Formula ? cell.CachedFormulaResultType : cell.CellType;
            switch (type)
            {
                case CellType.String:
                    return cell.StringCellValue ?? string.Empty;
                case CellType.Boolean:
                    return cell.BooleanCellValue ? "true" : "false";
                case CellType.Numeric:
                    return cell.NumericCellValue.ToString("0.############################", CultureInfo.InvariantCulture);
                case CellType.Error:
                    return FormulaError.ForInt(cell.ErrorCellValue).String;
                default:
                    return string.Empty;
            }
        }

        private static void ValidateRequired(
            string value,
            int zeroBasedRow,
            string column,
            ICollection<ModelExcelImportError> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add(new ModelExcelImportError(zeroBasedRow + 1, column, "不能为空。"));
        }

        private static void ValidateLength(
            string value,
            int maximum,
            int zeroBasedRow,
            string column,
            ICollection<ModelExcelImportError> errors)
        {
            if (value != null && value.Length > maximum)
                errors.Add(new ModelExcelImportError(zeroBasedRow + 1, column, "长度不能超过 " + maximum + " 个字符。"));
        }

        private static void ValidateConsistent(
            string expected,
            string actual,
            int zeroBasedRow,
            string column,
            ICollection<ModelExcelImportError> errors)
        {
            if (!string.Equals(expected ?? string.Empty, actual ?? string.Empty, StringComparison.Ordinal))
                errors.Add(new ModelExcelImportError(zeroBasedRow + 1, column, "同一模型的" + column + "不一致。"));
        }

        private static int ParseNonNegativeInteger(
            string value,
            int zeroBasedRow,
            string column,
            ICollection<ModelExcelImportError> errors)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= 0)
                return parsed;
            errors.Add(new ModelExcelImportError(zeroBasedRow + 1, column, "必须是大于等于 0 的整数。"));
            return 0;
        }

        private static bool ParseBoolean(
            string value,
            int zeroBasedRow,
            string column,
            ICollection<ModelExcelImportError> errors)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            switch (value.Trim().ToLowerInvariant())
            {
                case "是":
                case "true":
                case "1":
                    return true;
                case "否":
                case "false":
                case "0":
                    return false;
                default:
                    errors.Add(new ModelExcelImportError(zeroBasedRow + 1, column, "只允许填写是/否、true/false 或 1/0。"));
                    return false;
            }
        }

        private static void ValidateWorkbookRelationships(
            IList<ModelExcelImportModel> models,
            IList<ModelExcelImportError> errors)
        {
            var tableNames = new Dictionary<string, ModelExcelImportModel>(StringComparer.OrdinalIgnoreCase);
            var modelsByName = models.ToDictionary(model => model.ModelName, StringComparer.OrdinalIgnoreCase);
            foreach (ModelExcelImportModel model in models)
            {
                if (!string.IsNullOrWhiteSpace(model.TableName))
                {
                    if (tableNames.TryGetValue(model.TableName, out ModelExcelImportModel other))
                        errors.Add(new ModelExcelImportError(model.SourceRow, "表名", "与模型“" + other.ModelName + "”使用了重复表名。"));
                    else
                        tableNames.Add(model.TableName, model);
                }

                if (string.IsNullOrWhiteSpace(model.ParentModelName) || IsBaseEntity(model.ParentModelName))
                    continue;
                if (string.Equals(model.ParentModelName, model.ModelName, StringComparison.OrdinalIgnoreCase))
                    errors.Add(new ModelExcelImportError(model.SourceRow, "父模型", "模型不能继承自身。"));
            }

            foreach (ModelExcelImportModel start in models)
            {
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ModelExcelImportModel current = start;
                while (current != null && !string.IsNullOrWhiteSpace(current.ParentModelName) &&
                       !IsBaseEntity(current.ParentModelName) &&
                       modelsByName.TryGetValue(current.ParentModelName, out ModelExcelImportModel parent))
                {
                    if (!visited.Add(current.ModelName))
                    {
                        errors.Add(new ModelExcelImportError(start.SourceRow, "父模型", "父模型引用形成循环。"));
                        break;
                    }
                    current = parent;
                }
            }
        }

        private static void ValidateAgainstDatabase(
            IEnumerable<ModelExcelImportModel> models,
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            IList<ModelExcelImportError> errors)
        {
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT ModelName, TableName FROM DataModels";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingNames.Add(reader.GetString(0));
                        existingTables.Add(reader.GetString(1));
                    }
                }
            }

            var importNames = new HashSet<string>(models.Select(model => model.ModelName), StringComparer.OrdinalIgnoreCase);
            foreach (ModelExcelImportModel model in models)
            {
                if (existingNames.Contains(model.ModelName))
                    errors.Add(new ModelExcelImportError(model.SourceRow, "模型名称", "数据库中已存在模型“" + model.ModelName + "”。"));
                if (existingTables.Contains(model.TableName))
                    errors.Add(new ModelExcelImportError(model.SourceRow, "表名", "数据库中已存在表名“" + model.TableName + "”。"));
                if (!string.IsNullOrWhiteSpace(model.ParentModelName) && !IsBaseEntity(model.ParentModelName) &&
                    !existingNames.Contains(model.ParentModelName) && !importNames.Contains(model.ParentModelName))
                {
                    errors.Add(new ModelExcelImportError(model.SourceRow, "父模型", "找不到父模型“" + model.ParentModelName + "”。"));
                }
            }
        }

        private static Dictionary<string, int> LoadExistingModelIds(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT Id, ModelName FROM DataModels";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read()) ids[reader.GetString(1)] = reader.GetInt32(0);
                }
            }
            return ids;
        }

        private static int InsertModel(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            ModelExcelImportModel model,
            int parentId)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO DataModels (ModelName, TableName, ParentModelId, Description, IsActive)
VALUES (@ModelName, @TableName, @ParentModelId, @Description, @IsActive);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@ModelName", model.ModelName);
                command.Parameters.AddWithValue("@TableName", model.TableName);
                command.Parameters.AddWithValue("@ParentModelId", parentId);
                command.Parameters.AddWithValue("@Description", model.Description ?? string.Empty);
                command.Parameters.AddWithValue("@IsActive", model.IsActive ? 1 : 0);
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private static void InsertField(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId,
            ModelExcelImportField field)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO ModelFields
    (ModelId, FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description)
VALUES
    (@ModelId, @FieldName, @FieldType, @FieldLength, @IsRequired, @IsPrimaryKey, @IsIdentity, @Description);";
                command.Parameters.AddWithValue("@ModelId", modelId);
                command.Parameters.AddWithValue("@FieldName", field.FieldName);
                command.Parameters.AddWithValue("@FieldType", field.FieldType);
                command.Parameters.AddWithValue("@FieldLength", field.FieldLength);
                command.Parameters.AddWithValue("@IsRequired", field.IsRequired ? 1 : 0);
                command.Parameters.AddWithValue("@IsPrimaryKey", field.IsPrimaryKey ? 1 : 0);
                command.Parameters.AddWithValue("@IsIdentity", field.IsIdentity ? 1 : 0);
                command.Parameters.AddWithValue("@Description", field.Description ?? string.Empty);
                command.ExecuteNonQuery();
            }
        }

        private static void UpdateParentId(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId,
            int parentId)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE DataModels SET ParentModelId = @ParentModelId WHERE Id = @Id";
                command.Parameters.AddWithValue("@ParentModelId", parentId);
                command.Parameters.AddWithValue("@Id", modelId);
                command.ExecuteNonQuery();
            }
        }

        private static int ResolveExistingParentId(string parentName, IDictionary<string, int> existingIds)
        {
            if (string.IsNullOrWhiteSpace(parentName)) return 0;
            if (IsBaseEntity(parentName)) return -2;
            return existingIds.TryGetValue(parentName, out int parentId) ? parentId : 0;
        }

        private static int ResolveParentId(
            string parentName,
            IDictionary<string, int> existingIds,
            IDictionary<string, int> importedIds)
        {
            if (string.IsNullOrWhiteSpace(parentName)) return 0;
            if (IsBaseEntity(parentName)) return -2;
            if (importedIds.TryGetValue(parentName, out int importedId)) return importedId;
            if (existingIds.TryGetValue(parentName, out int existingId)) return existingId;
            throw new InvalidOperationException("父模型不存在：" + parentName);
        }

        private static bool IsBaseEntity(string value)
        {
            return string.Equals(value, "BaseEntity", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ModelExcelImportPreview
    {
        internal ModelExcelImportPreview(
            string filePath,
            IList<ModelExcelImportModel> models,
            IList<ModelExcelImportError> errors)
        {
            FilePath = filePath ?? string.Empty;
            Models = new List<ModelExcelImportModel>(models);
            Errors = new List<ModelExcelImportError>(errors);
        }

        public string FilePath { get; }
        public IReadOnlyList<ModelExcelImportModel> Models { get; }
        public IReadOnlyList<ModelExcelImportError> Errors { get; }
        public int ModelCount => Models.Count;
        public int FieldCount => Models.Sum(model => model.Fields.Count);
        public int EnabledModelCount => Models.Count(model => model.IsActive);
        public bool CanImport => Models.Count > 0 && Errors.Count == 0;
    }

    public sealed class ModelExcelImportModel
    {
        internal ModelExcelImportModel(
            int sourceRow,
            string modelName,
            string tableName,
            string description,
            string parentModelName,
            bool isActive)
        {
            SourceRow = sourceRow;
            ModelName = modelName;
            TableName = tableName;
            Description = description;
            ParentModelName = parentModelName;
            IsActive = isActive;
            MutableFields = new List<ModelExcelImportField>();
        }

        public int SourceRow { get; }
        public string ModelName { get; }
        public string TableName { get; }
        public string Description { get; }
        public string ParentModelName { get; }
        public bool IsActive { get; }
        public IReadOnlyList<ModelExcelImportField> Fields => MutableFields;
        internal List<ModelExcelImportField> MutableFields { get; }
    }

    public sealed class ModelExcelImportField
    {
        internal ModelExcelImportField(
            int sourceRow,
            string fieldName,
            string fieldType,
            int fieldLength,
            bool isRequired,
            bool isPrimaryKey,
            bool isIdentity,
            string description)
        {
            SourceRow = sourceRow;
            FieldName = fieldName;
            FieldType = fieldType;
            FieldLength = fieldLength;
            IsRequired = isRequired;
            IsPrimaryKey = isPrimaryKey;
            IsIdentity = isIdentity;
            Description = description;
        }

        public int SourceRow { get; }
        public string FieldName { get; }
        public string FieldType { get; }
        public int FieldLength { get; }
        public bool IsRequired { get; }
        public bool IsPrimaryKey { get; }
        public bool IsIdentity { get; }
        public string Description { get; }
    }

    public sealed class ModelExcelImportError
    {
        public ModelExcelImportError(int rowNumber, string columnName, string message)
        {
            RowNumber = rowNumber;
            ColumnName = columnName ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public int RowNumber { get; }
        public string ColumnName { get; }
        public string Message { get; }

        public override string ToString()
        {
            string row = RowNumber > 0 ? "第 " + RowNumber + " 行" : "文件";
            return row + "【" + ColumnName + "】：" + Message;
        }
    }

    public sealed class ModelExcelImportedModel
    {
        internal ModelExcelImportedModel(int id, ModelExcelImportModel model)
        {
            Id = id;
            Model = model;
        }

        public int Id { get; }
        public ModelExcelImportModel Model { get; }
    }

    public sealed class ModelExcelImportResult
    {
        internal ModelExcelImportResult(IList<ModelExcelImportedModel> models)
        {
            Models = new List<ModelExcelImportedModel>(models);
        }

        public IReadOnlyList<ModelExcelImportedModel> Models { get; }
        public int ModelCount => Models.Count;
        public int FieldCount => Models.Sum(item => item.Model.Fields.Count);
        public int FirstModelId => Models.Count == 0 ? -1 : Models[0].Id;
    }

    public sealed class ModelExcelImportValidationException : InvalidOperationException
    {
        public ModelExcelImportValidationException(IEnumerable<ModelExcelImportError> errors)
            : base("Excel 数据模型导入校验失败。")
        {
            Errors = new List<ModelExcelImportError>(errors ?? Enumerable.Empty<ModelExcelImportError>());
        }

        public IReadOnlyList<ModelExcelImportError> Errors { get; }
    }
}
