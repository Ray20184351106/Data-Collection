using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class ModelSchemaField
    {
        public string FieldName { get; set; }
        public string FieldType { get; set; }
        public int FieldLength { get; set; }
        public bool IsRequired { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public string Description { get; set; }
    }

    public sealed class ModelSchemaService
    {
        private readonly string _connectionString;

        public ModelSchemaService(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public IReadOnlyList<ModelSchemaField> LoadFields(int modelId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                return LoadFields(connection, null, modelId);
            }
        }

        public IReadOnlyList<ModelSchemaField> LoadFields(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var fields = new List<ModelSchemaField>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description
FROM ModelFields
WHERE ModelId = @ModelId
ORDER BY FieldName COLLATE BINARY;";
                command.Parameters.AddWithValue("@ModelId", modelId);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        fields.Add(new ModelSchemaField
                        {
                            FieldName = reader.GetString(0),
                            FieldType = reader.GetString(1),
                            FieldLength = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            IsRequired = !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
                            IsPrimaryKey = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                            IsIdentity = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                            Description = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
                        });
                    }
                }
            }
            return fields;
        }

        public string ComputeHash(int modelId)
        {
            return ComputeHash(LoadFields(modelId));
        }

        public string LoadModelName(int modelId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                return LoadModelName(connection, null, modelId);
            }
        }

        public string LoadModelName(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT ModelName FROM DataModels WHERE Id = @ModelId;";
                command.Parameters.AddWithValue("@ModelId", modelId);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        public static string ComputeHash(IEnumerable<ModelSchemaField> fields)
        {
            string canonical = string.Join("\n", (fields ?? Enumerable.Empty<ModelSchemaField>())
                .OrderBy(field => field.FieldName, StringComparer.Ordinal)
                .Select(field => string.Join("|", new[]
                {
                    field.FieldName ?? string.Empty,
                    (field.FieldType ?? string.Empty).Trim().ToLowerInvariant(),
                    field.FieldLength.ToString(CultureInfo.InvariantCulture),
                    field.IsRequired ? "1" : "0",
                    field.IsPrimaryKey ? "1" : "0",
                    field.IsIdentity ? "1" : "0"
                })));
            return MappingRuleSerializer.Sha256(canonical);
        }

        public bool HasPublishedBinding(int modelId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                return HasPublishedBinding(connection, null, modelId);
            }
        }

        public bool HasPublishedBinding(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT COUNT(*)
FROM PublishedParseRuleBindings b
INNER JOIN ParseRuleVersions v ON v.Id = b.ParseRuleVersionId
INNER JOIN ParseRuleDefinitions d ON d.Id = v.DefinitionId
WHERE d.ModelId = @ModelId AND v.Status = @PublishedStatus;";
                command.Parameters.AddWithValue("@ModelId", modelId);
                command.Parameters.AddWithValue("@PublishedStatus", (int)ParseRuleStatus.Published);
                if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    return true;
            }
            if (!TableExists(connection, transaction, "ParseRuleVersionModels")) return false;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT COUNT(*)
FROM PublishedParseRuleBindings b
INNER JOIN ParseRuleVersions v ON v.Id=b.ParseRuleVersionId
INNER JOIN ParseRuleVersionModels m ON m.ParseRuleVersionId=v.Id
WHERE m.ModelId=@ModelId AND v.Status=@PublishedStatus;";
                command.Parameters.AddWithValue("@ModelId", modelId);
                command.Parameters.AddWithValue("@PublishedStatus", (int)ParseRuleStatus.Published);
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        public string LoadTableName(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT TableName FROM DataModels WHERE Id = @ModelId;";
                command.Parameters.AddWithValue("@ModelId", modelId);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? null
                    : Convert.ToString(value, CultureInfo.InvariantCulture);
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
                command.Parameters.AddWithValue("@Name", tableName);
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        public static bool HasDestructiveChange(
            IEnumerable<ModelSchemaField> existing,
            IEnumerable<ModelSchemaField> proposed)
        {
            // Published versions are keyed by the complete model schema hash. Allowing
            // even an additive field change here would make the active version fail its
            // runtime schema gate immediately, so any hash-changing edit is treated as
            // destructive until a staged model-version workflow exists.
            return !string.Equals(ComputeHash(existing), ComputeHash(proposed), StringComparison.Ordinal);
        }

        public void InvalidateUnpublishedValidation(int modelId, string currentModelHash)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    InvalidateUnpublishedValidation(connection, transaction, modelId, currentModelHash);
                    transaction.Commit();
                }
            }
        }

        public void InvalidateUnpublishedValidation(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId,
            string currentModelHash)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE ParseRuleVersions
SET Status = @DraftStatus,
    Revision = Revision + 1,
    ValidationSummary = NULL,
    ValidatedTime = NULL
WHERE DefinitionId IN (SELECT Id FROM ParseRuleDefinitions WHERE ModelId = @ModelId)
  AND Status = @ValidatedStatus
  AND ModelSchemaHash <> @CurrentModelHash;";
                command.Parameters.AddWithValue("@DraftStatus", (int)ParseRuleStatus.Draft);
                command.Parameters.AddWithValue("@ValidatedStatus", (int)ParseRuleStatus.Validated);
                command.Parameters.AddWithValue("@ModelId", modelId);
                command.Parameters.AddWithValue("@CurrentModelHash", currentModelHash ?? string.Empty);
                command.ExecuteNonQuery();
            }
        }
    }
}
