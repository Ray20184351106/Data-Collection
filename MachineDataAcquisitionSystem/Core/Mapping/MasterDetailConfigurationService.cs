using System;
using System.Data;
using System.Data.SQLite;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class MasterDetailConfigurationService
    {
        private readonly string _connectionString;

        public MasterDetailConfigurationService(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public RelationFieldProvisionResult EnsureRelationField(int detailModelId, string fieldName)
        {
            if (detailModelId <= 0) throw new ArgumentOutOfRangeException(nameof(detailModelId));
            if (!MappingRuleSerializer.IsIdentifier(fieldName) ||
                string.Equals(fieldName, "CID", StringComparison.OrdinalIgnoreCase))
                throw new MappingValidationException("关联字段必须是非 CID 的合法标识符。");
            if (fieldName.Length > 128)
                throw new MappingValidationException("关联字段名称超过长度上限。");

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (SQLiteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureActiveModel(connection, transaction, detailModelId);
                    RelationFieldProvisionResult existing = LoadExisting(
                        connection,
                        transaction,
                        detailModelId,
                        fieldName);
                    if (existing != null)
                    {
                        transaction.Commit();
                        return existing;
                    }
                    if (HasPublishedDependency(connection, transaction, detailModelId))
                        throw new ParseRuleStateException(
                            "子模型已有已发布解析规则，不能自动增加关联字段。请复制为新模型后再配置主子表规则。");

                    using (SQLiteCommand command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"
INSERT INTO ModelFields
    (ModelId,FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,
     Description,IsSystemGenerated,SystemRole)
VALUES
    (@ModelId,@FieldName,'long',0,0,0,0,'主表 CID 关联字段',1,'ParentCid');";
                        command.Parameters.Add("@ModelId", DbType.Int32).Value = detailModelId;
                        command.Parameters.Add("@FieldName", DbType.String).Value = fieldName;
                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                    return new RelationFieldProvisionResult(detailModelId, fieldName, true);
                }
            }
        }

        private static RelationFieldProvisionResult LoadExisting(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId,
            string fieldName)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT FieldType,IsRequired,IsPrimaryKey,IsIdentity
FROM ModelFields
WHERE ModelId=@ModelId AND FieldName=@FieldName;";
                command.Parameters.Add("@ModelId", DbType.Int32).Value = modelId;
                command.Parameters.Add("@FieldName", DbType.String).Value = fieldName;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    string fieldType = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    bool isRequired = !reader.IsDBNull(1) && reader.GetInt32(1) != 0;
                    bool isPrimaryKey = !reader.IsDBNull(2) && reader.GetInt32(2) != 0;
                    bool isIdentity = !reader.IsDBNull(3) && reader.GetInt32(3) != 0;
                    if (!string.Equals(fieldType, "long", StringComparison.OrdinalIgnoreCase) ||
                        isRequired || isPrimaryKey || isIdentity)
                        throw new MappingValidationException(
                            "已有同名关联字段必须是可空 long 类型、非主键且非自增字段：" + fieldName);
                    return new RelationFieldProvisionResult(modelId, fieldName, false);
                }
            }
        }

        private static void EnsureActiveModel(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT COUNT(1) FROM DataModels WHERE Id=@Id AND IsActive=1;";
                command.Parameters.Add("@Id", DbType.Int32).Value = modelId;
                if (Convert.ToInt32(command.ExecuteScalar()) != 1)
                    throw new MappingValidationException("未找到启用的子模型。");
            }
        }

        private static bool HasPublishedDependency(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId)
        {
            if (!TableExists(connection, transaction, "PublishedParseRuleBindings") ||
                !TableExists(connection, transaction, "ParseRuleVersions") ||
                !TableExists(connection, transaction, "ParseRuleDefinitions"))
                return false;
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT COUNT(1)
FROM PublishedParseRuleBindings b
INNER JOIN ParseRuleVersions v ON v.Id=b.ParseRuleVersionId
INNER JOIN ParseRuleDefinitions d ON d.Id=v.DefinitionId
WHERE v.Status=@Status AND d.ModelId=@ModelId;";
                command.Parameters.Add("@Status", DbType.Int32).Value = (int)ParseRuleStatus.Published;
                command.Parameters.Add("@ModelId", DbType.Int32).Value = modelId;
                if (Convert.ToInt32(command.ExecuteScalar()) > 0) return true;
            }

            if (!TableExists(connection, transaction, "ParseRuleVersionModels")) return false;
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT COUNT(1)
FROM PublishedParseRuleBindings b
INNER JOIN ParseRuleVersions v ON v.Id=b.ParseRuleVersionId
INNER JOIN ParseRuleVersionModels m ON m.ParseRuleVersionId=v.Id
WHERE v.Status=@Status AND m.ModelId=@ModelId;";
                command.Parameters.Add("@Status", DbType.Int32).Value = (int)ParseRuleStatus.Published;
                command.Parameters.Add("@ModelId", DbType.Int32).Value = modelId;
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private static bool TableExists(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string tableName)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@Name;";
                command.Parameters.Add("@Name", DbType.String).Value = tableName;
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }
    }

    public sealed class RelationFieldProvisionResult
    {
        public RelationFieldProvisionResult(int modelId, string fieldName, bool created)
        {
            ModelId = modelId;
            FieldName = fieldName;
            Created = created;
        }

        public int ModelId { get; private set; }
        public string FieldName { get; private set; }
        public bool Created { get; private set; }
    }
}
