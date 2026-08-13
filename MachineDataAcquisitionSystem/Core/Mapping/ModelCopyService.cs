using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class ModelCopyResult
    {
        public int ModelId { get; set; }
        public string ModelName { get; set; }
        public string TableName { get; set; }
    }

    /// <summary>
    /// Creates an independent, inactive copy of a model and its field definitions.
    /// Parse rules, scripts, machine bindings, and target-table data are not copied.
    /// </summary>
    public sealed class ModelCopyService
    {
        private const int MaximumIdentifierLength = 128;
        private readonly string _connectionString;

        public ModelCopyService(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path is required.", nameof(databasePath));

            _connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(databasePath),
                Version = 3,
                ForeignKeys = true,
                BusyTimeout = 5000,
                Pooling = false
            }.ConnectionString;
        }

        public ModelCopyResult CopyModel(int sourceModelId)
        {
            if (sourceModelId <= 0) throw new ArgumentOutOfRangeException(nameof(sourceModelId));

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    SourceModel source = LoadSource(connection, transaction, sourceModelId);
                    int copyNumber = FindAvailableCopyNumber(connection, transaction, source.ModelName, source.TableName);
                    string modelName = BuildCopyName(source.ModelName, copyNumber);
                    string tableName = BuildCopyName(source.TableName, copyNumber);

                    int newModelId = InsertModel(connection, transaction, source, modelName, tableName);
                    CopyFields(connection, transaction, sourceModelId, newModelId);
                    transaction.Commit();

                    return new ModelCopyResult
                    {
                        ModelId = newModelId,
                        ModelName = modelName,
                        TableName = tableName
                    };
                }
            }
        }

        private static SourceModel LoadSource(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int sourceModelId)
        {
            using (var command = new SQLiteCommand(@"
SELECT ModelName,TableName,ParentModelId,Description
FROM DataModels
WHERE Id=@Id;", connection, transaction))
            {
                command.Parameters.Add("@Id", DbType.Int32).Value = sourceModelId;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        throw new InvalidOperationException("数据模型不存在或已删除。");

                    return new SourceModel
                    {
                        ModelName = reader.GetString(0),
                        TableName = reader.GetString(1),
                        ParentModelId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                    };
                }
            }
        }

        private static int FindAvailableCopyNumber(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string sourceModelName,
            string sourceTableName)
        {
            for (int copyNumber = 1; copyNumber < int.MaxValue; copyNumber++)
            {
                string modelName = BuildCopyName(sourceModelName, copyNumber);
                string tableName = BuildCopyName(sourceTableName, copyNumber);
                using (var command = new SQLiteCommand(@"
SELECT 1
FROM DataModels
WHERE ModelName=@ModelName OR TableName=@TableName
LIMIT 1;", connection, transaction))
                {
                    command.Parameters.Add("@ModelName", DbType.String).Value = modelName;
                    command.Parameters.Add("@TableName", DbType.String).Value = tableName;
                    if (command.ExecuteScalar() == null) return copyNumber;
                }
            }

            throw new InvalidOperationException("无法生成可用的模型副本名称。");
        }

        private static string BuildCopyName(string original, int copyNumber)
        {
            string suffix = copyNumber == 1
                ? "_Copy"
                : "_Copy" + copyNumber.ToString(CultureInfo.InvariantCulture);
            string safeOriginal = original ?? string.Empty;
            int maximumOriginalLength = MaximumIdentifierLength - suffix.Length;
            if (safeOriginal.Length > maximumOriginalLength)
                safeOriginal = safeOriginal.Substring(0, maximumOriginalLength);
            return safeOriginal + suffix;
        }

        private static int InsertModel(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            SourceModel source,
            string modelName,
            string tableName)
        {
            using (var command = new SQLiteCommand(@"
INSERT INTO DataModels (ModelName,TableName,ParentModelId,Description,IsActive)
VALUES (@ModelName,@TableName,@ParentModelId,@Description,0);", connection, transaction))
            {
                command.Parameters.Add("@ModelName", DbType.String).Value = modelName;
                command.Parameters.Add("@TableName", DbType.String).Value = tableName;
                command.Parameters.Add("@ParentModelId", DbType.Int32).Value = source.ParentModelId;
                command.Parameters.Add("@Description", DbType.String).Value = source.Description;
                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand("SELECT last_insert_rowid();", connection, transaction))
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void CopyFields(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int sourceModelId,
            int targetModelId)
        {
            using (var command = new SQLiteCommand(@"
INSERT INTO ModelFields
    (ModelId,FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,Description)
SELECT
    @TargetModelId,FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,Description
FROM ModelFields
WHERE ModelId=@SourceModelId
ORDER BY Id;", connection, transaction))
            {
                command.Parameters.Add("@TargetModelId", DbType.Int32).Value = targetModelId;
                command.Parameters.Add("@SourceModelId", DbType.Int32).Value = sourceModelId;
                command.ExecuteNonQuery();
            }
        }

        private sealed class SourceModel
        {
            public string ModelName { get; set; }
            public string TableName { get; set; }
            public int ParentModelId { get; set; }
            public string Description { get; set; }
        }
    }
}
