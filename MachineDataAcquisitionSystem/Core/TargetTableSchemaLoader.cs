using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace MachineDataAcquisitionSystem.Core
{
    public sealed class TargetTableSchemaLoader
    {
        private readonly string _connectionString;

        public TargetTableSchemaLoader(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public TargetTableDefinition Load(int modelId)
        {
            if (modelId <= 0) throw new ArgumentOutOfRangeException(nameof(modelId));
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                var columns = new List<TargetTableColumnDefinition>();
                var visited = new HashSet<int>();
                string tableName = LoadModel(connection, modelId, columns, visited, true);
                var definition = new TargetTableDefinition
                {
                    TableName = tableName,
                    Columns = columns
                };
                definition.Validate();
                return definition;
            }
        }

        private static string LoadModel(
            SQLiteConnection connection,
            int modelId,
            List<TargetTableColumnDefinition> columns,
            HashSet<int> visited,
            bool isTarget)
        {
            if (!visited.Add(modelId))
                throw new InvalidOperationException("数据模型继承关系存在循环。");

            string tableName;
            int parentModelId;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT TableName,ParentModelId FROM DataModels WHERE Id=@Id AND IsActive=1;";
                command.Parameters.Add("@Id", DbType.Int32).Value = modelId;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("未找到启用的数据模型：" + modelId);
                    tableName = reader.GetString(0);
                    parentModelId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                }
            }

            if (parentModelId == -2)
                LoadBaseFields(connection, columns);
            else if (parentModelId > 0)
                LoadModel(connection, parentModelId, columns, visited, false);

            LoadModelFields(connection, modelId, columns);
            return isTarget ? tableName : null;
        }

        private static void LoadBaseFields(
            SQLiteConnection connection,
            ICollection<TargetTableColumnDefinition> columns)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT FieldName,FieldType,FieldLength,IsRequired,Description
FROM BaseFields ORDER BY SortOrder,Id;";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(new TargetTableColumnDefinition
                        {
                            FieldName = reader.GetString(0),
                            FieldType = reader.GetString(1),
                            FieldLength = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            IsRequired = !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
                            Description = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                        });
                    }
                }
            }
        }

        private static void LoadModelFields(
            SQLiteConnection connection,
            int modelId,
            ICollection<TargetTableColumnDefinition> columns)
        {
            bool hasSystemGenerated = HasColumn(connection, "ModelFields", "IsSystemGenerated");
            bool hasSystemRole = HasColumn(connection, "ModelFields", "SystemRole");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = string.Format(@"
SELECT FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,Description,
       {0} AS IsSystemGenerated,{1} AS SystemRole
FROM ModelFields WHERE ModelId=@ModelId ORDER BY Id;",
                    hasSystemGenerated ? "IsSystemGenerated" : "0",
                    hasSystemRole ? "SystemRole" : "NULL");
                command.Parameters.Add("@ModelId", DbType.Int32).Value = modelId;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(new TargetTableColumnDefinition
                        {
                            FieldName = reader.GetString(0),
                            FieldType = reader.GetString(1),
                            FieldLength = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            IsRequired = !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
                            IsPrimaryKey = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                            IsIdentity = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                            Description = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                            IsSystemGenerated = !reader.IsDBNull(7) && reader.GetInt32(7) != 0,
                            SystemRole = reader.IsDBNull(8) ? null : reader.GetString(8)
                        });
                    }
                }
            }
        }

        private static bool HasColumn(
            SQLiteConnection connection,
            string tableName,
            string columnName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"" + tableName + "\");";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
