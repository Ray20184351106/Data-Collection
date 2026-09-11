using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class ConfigurationDeletionBlockedException : InvalidOperationException
    {
        public ConfigurationDeletionBlockedException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Deletes design-time configuration after applying the reference rules for
    /// each configuration type. Mapping deletion also removes its published bindings.
    /// </summary>
    public sealed class ConfigurationDeletionService
    {
        private readonly string _databasePath;
        private readonly string _connectionString;

        public ConfigurationDeletionService(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path is required.", nameof(databasePath));

            _databasePath = Path.GetFullPath(databasePath);
            _connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Version = 3,
                ForeignKeys = true,
                BusyTimeout = 5000,
                Pooling = false
            }.ConnectionString;
        }

        public void DeleteModel(int modelId)
        {
            if (modelId <= 0) throw new ArgumentOutOfRangeException(nameof(modelId));
            EnsureRuleSchema();

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                RequireExists(connection, transaction, "SELECT 1 FROM DataModels WHERE Id=@Id;", modelId, "数据模型不存在或已删除。");
                EnsureNoRows(connection, transaction,
                    "SELECT 1 FROM DataModels WHERE ParentModelId=@Id LIMIT 1;", modelId,
                    "该数据模型被其他模型继承，不能删除。");
                EnsureNoRows(connection, transaction,
                    "SELECT 1 FROM ParseScripts WHERE ModelId=@Id LIMIT 1;", modelId,
                    "该数据模型仍被解析脚本引用，不能删除。");
                EnsureNoRows(connection, transaction,
                    "SELECT 1 FROM ParseRuleDefinitions WHERE ModelId=@Id LIMIT 1;", modelId,
                    "该数据模型仍被字段映射或规则版本引用，不能删除。");

                Execute(connection, transaction, "DELETE FROM ModelFields WHERE ModelId=@Id;", modelId);
                if (Execute(connection, transaction, "DELETE FROM DataModels WHERE Id=@Id;", modelId) != 1)
                    throw new ConfigurationDeletionBlockedException("数据模型删除失败，请刷新后重试。");
                transaction.Commit();
            }
        }

        public void DeleteLegacyScript(long scriptId)
        {
            if (scriptId <= 0) throw new ArgumentOutOfRangeException(nameof(scriptId));
            EnsureRuleSchema();

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                RequireExists(connection, transaction, "SELECT 1 FROM ParseScripts WHERE Id=@Id;", scriptId, "解析脚本不存在或已删除。");
                long definitionId = FindLegacyDefinitionId(connection, transaction, scriptId);
                if (definitionId > 0)
                {
                    EnsureNoRows(connection, transaction, @"
SELECT 1 FROM PublishedParseRuleBindings b
INNER JOIN ParseRuleVersions v ON v.Id=b.ParseRuleVersionId
WHERE v.DefinitionId=@DefinitionId LIMIT 1;", definitionId,
                        "该解析脚本已经发布到机台，不能直接删除。请先停用脚本并保存，或先发布替代规则。");
                    DeleteDefinitionRows(connection, transaction, definitionId);
                }

                Execute(connection, transaction, "DELETE FROM FieldMappings WHERE ScriptId=@Id;", scriptId);
                Execute(connection, transaction, "DELETE FROM ScriptMachines WHERE ScriptId=@Id;", scriptId);
                if (Execute(connection, transaction, "DELETE FROM ParseScripts WHERE Id=@Id;", scriptId) != 1)
                    throw new ConfigurationDeletionBlockedException("解析脚本删除失败，请刷新后重试。");
                transaction.Commit();
            }
        }

        public void DeleteMappingDefinition(long definitionId)
        {
            if (definitionId <= 0) throw new ArgumentOutOfRangeException(nameof(definitionId));
            EnsureRuleSchema();

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                RequireExists(connection, transaction,
                    @"SELECT 1
FROM ParseRuleDefinitions d
INNER JOIN ParseRuleVersions v ON v.DefinitionId=d.Id
WHERE d.Id=@Id AND v.RuleType=0
LIMIT 1;", definitionId, "字段映射不存在或已删除。");
                Execute(connection, transaction, @"
DELETE FROM PublishedParseRuleBindings
WHERE ParseRuleVersionId IN (
    SELECT Id FROM ParseRuleVersions WHERE DefinitionId=@Id
);", definitionId);
                DeleteDefinitionRows(connection, transaction, definitionId);
                transaction.Commit();
            }
        }

        private void EnsureRuleSchema()
        {
            using (var store = new ParseRuleStore(_databasePath))
                store.Initialize();
        }

        private SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static void DeleteDefinitionRows(SQLiteConnection connection, SQLiteTransaction transaction, long definitionId)
        {
            Execute(connection, transaction, @"
DELETE FROM ParseRuleVersionModels
WHERE ParseRuleVersionId IN (
    SELECT Id FROM ParseRuleVersions WHERE DefinitionId=@Id
);", definitionId);
            Execute(connection, transaction, "DELETE FROM ParseRuleVersions WHERE DefinitionId=@Id;", definitionId);
            if (Execute(connection, transaction, "DELETE FROM ParseRuleDefinitions WHERE Id=@Id;", definitionId) != 1)
                throw new ConfigurationDeletionBlockedException("规则删除失败，请刷新后重试。");
        }

        private static long FindLegacyDefinitionId(SQLiteConnection connection, SQLiteTransaction transaction, long scriptId)
        {
            if (!ColumnExists(connection, transaction, "ParseRuleDefinitions", "LegacyScriptId")) return 0;
            using (var command = new SQLiteCommand(
                "SELECT Id FROM ParseRuleDefinitions WHERE LegacyScriptId=@Id;", connection, transaction))
            {
                command.Parameters.Add("@Id", DbType.Int64).Value = scriptId;
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        private static bool ColumnExists(SQLiteConnection connection, SQLiteTransaction transaction, string table, string column)
        {
            using (var command = new SQLiteCommand("PRAGMA table_info(" + table + ");", connection, transaction))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (!reader.IsDBNull(1) && string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static void RequireExists(SQLiteConnection connection, SQLiteTransaction transaction, string sql, long id, string message)
        {
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.Add("@Id", DbType.Int64).Value = id;
                if (command.ExecuteScalar() == null) throw new ConfigurationDeletionBlockedException(message);
            }
        }

        private static void EnsureNoRows(SQLiteConnection connection, SQLiteTransaction transaction, string sql, long id, string message)
        {
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.Add("@Id", DbType.Int64).Value = id;
                command.Parameters.Add("@DefinitionId", DbType.Int64).Value = id;
                if (command.ExecuteScalar() != null) throw new ConfigurationDeletionBlockedException(message);
            }
        }

        private static int Execute(SQLiteConnection connection, SQLiteTransaction transaction, string sql, long id)
        {
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.Add("@Id", DbType.Int64).Value = id;
                return command.ExecuteNonQuery();
            }
        }
    }
}
