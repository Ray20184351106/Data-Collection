using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class LegacyScriptSaveRequest
    {
        public LegacyScriptSaveRequest()
        {
            MachineIds = new List<int>();
        }

        public long LegacyScriptId { get; set; }
        public string Name { get; set; }
        public int ModelId { get; set; }
        public string TargetModelType { get; set; }
        public string FileExtension { get; set; }
        public string ScriptCode { get; set; }
        public bool IsEnabled { get; set; }
        public IReadOnlyCollection<int> MachineIds { get; set; }
    }

    public sealed class LegacyScriptSaveResult
    {
        public long LegacyScriptId { get; set; }
        public long DefinitionId { get; set; }
        public long ParseRuleVersionId { get; set; }
    }

    public sealed class LegacyScriptVersionService
    {
        private readonly string _databasePath;
        private readonly string _connectionString;

        public LegacyScriptVersionService(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("Database path is required.", nameof(databasePath));
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

        public LegacyScriptSaveResult Save(LegacyScriptSaveRequest request)
        {
            Validate(request);
            string extension = NormalizeExtension(request.FileExtension);
            List<int> machines = (request.MachineIds ?? new List<int>())
                .Distinct()
                .OrderBy(value => value)
                .ToList();
            DateTime now = DateTime.UtcNow;

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    string modelHash = LoadAndValidateModelSchemaHash(
                        connection,
                        transaction,
                        request.ModelId,
                        request.TargetModelType);
                    long legacyScriptId = request.LegacyScriptId;
                    long definitionId = legacyScriptId > 0
                        ? FindDefinitionId(connection, transaction, legacyScriptId)
                        : 0;

                    if (definitionId > 0)
                        EnsureStableDefinition(connection, transaction, definitionId, request.ModelId, request.TargetModelType, extension);

                    if (legacyScriptId <= 0)
                    {
                        legacyScriptId = InsertLegacyScript(connection, transaction, request, extension);
                    }
                    else
                    {
                        UpdateLegacyScript(connection, transaction, request, extension);
                    }

                    if (definitionId == 0)
                    {
                        definitionId = InsertDefinition(
                            connection, transaction, legacyScriptId, request, extension, now);
                    }
                    else
                    {
                        UpdateDefinitionName(connection, transaction, definitionId, request.Name, now);
                    }

                    if (request.IsEnabled)
                    {
                        foreach (int machineId in machines)
                            EnsureBindingAvailable(connection, transaction, definitionId, machineId, extension);
                    }

                    ReplaceLegacyMachineRows(connection, transaction, legacyScriptId, machines);

                    int versionNumber;
                    int revision;
                    LoadNextVersionNumbers(connection, transaction, definitionId, out versionNumber, out revision);
                    bool publish = request.IsEnabled && machines.Count > 0;
                    long versionId = InsertVersion(
                        connection,
                        transaction,
                        definitionId,
                        versionNumber,
                        revision,
                        legacyScriptId,
                        request.ScriptCode,
                        modelHash,
                        publish,
                        now);

                    RemoveDefinitionBindings(connection, transaction, definitionId);
                    SupersedeOldVersions(connection, transaction, definitionId, versionId);
                    if (publish)
                    {
                        foreach (int machineId in machines)
                            InsertBinding(connection, transaction, machineId, extension, versionId, now);
                    }

                    transaction.Commit();
                    return new LegacyScriptSaveResult
                    {
                        LegacyScriptId = legacyScriptId,
                        DefinitionId = definitionId,
                        ParseRuleVersionId = versionId
                    };
                }
            }
        }

        private static void Validate(LegacyScriptSaveRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.LegacyScriptId < 0) throw new MappingValidationException("LegacyScriptId cannot be negative.");
            if (string.IsNullOrWhiteSpace(request.Name)) throw new MappingValidationException("Script name is required.");
            if (request.ModelId <= 0) throw new MappingValidationException("ModelId must be positive.");
            if (!MappingRuleSerializer.IsIdentifier(request.TargetModelType))
                throw new MappingValidationException("Target model type is invalid.");
            if (string.IsNullOrWhiteSpace(request.ScriptCode)) throw new MappingValidationException("Script code is required.");
            if (request.ScriptCode.Length > 1024 * 1024) throw new MappingValidationException("Script code is too large.");
            if ((request.MachineIds ?? new List<int>()).Any(machine => machine <= 0))
                throw new MappingValidationException("Machine ids must be positive.");
        }

        private static string LoadAndValidateModelSchemaHash(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId,
            string targetModelType)
        {
            using (var command = CreateCommand(connection, transaction,
                "SELECT ModelName FROM DataModels WHERE Id=@ModelId;"))
            {
                command.Parameters.AddWithValue("@ModelId", modelId);
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                    throw new MappingValidationException("The selected model does not exist.");
                if (!string.Equals(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    targetModelType,
                    StringComparison.Ordinal))
                    throw new MappingValidationException("The selected model name changed; reload before saving the script.");
            }

            var fields = new List<ModelSchemaField>();
            using (var command = CreateCommand(connection, transaction, @"
SELECT FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,Description
FROM ModelFields
WHERE ModelId=@ModelId;"))
            {
                command.Parameters.AddWithValue("@ModelId", modelId);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        fields.Add(new ModelSchemaField
                        {
                            FieldName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                            FieldType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            FieldLength = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            IsRequired = !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
                            IsPrimaryKey = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                            IsIdentity = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                            Description = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
                        });
                    }
                }
            }
            if (fields.Count == 0)
                throw new MappingValidationException("The selected model has no fields.");
            return ModelSchemaService.ComputeHash(fields);
        }

        private static string NormalizeExtension(string value)
        {
            string extension = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (!extension.StartsWith(".", StringComparison.Ordinal)) extension = "." + extension;
            if (extension.Length < 2 || extension.Length > 16 ||
                extension.Skip(1).Any(character => !char.IsLetterOrDigit(character)))
                throw new MappingValidationException("File extension is invalid.");
            return extension;
        }

        private static long FindDefinitionId(SQLiteConnection connection, SQLiteTransaction transaction, long legacyScriptId)
        {
            using (var command = CreateCommand(connection, transaction, "SELECT Id FROM ParseRuleDefinitions WHERE LegacyScriptId=@LegacyScriptId;"))
            {
                command.Parameters.AddWithValue("@LegacyScriptId", legacyScriptId);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        private static void EnsureStableDefinition(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId,
            int modelId,
            string targetModelType,
            string extension)
        {
            using (var command = CreateCommand(connection, transaction, @"
SELECT ModelId, TargetModelType, NormalizedExtension
FROM ParseRuleDefinitions WHERE Id=@DefinitionId;"))
            {
                command.Parameters.AddWithValue("@DefinitionId", definitionId);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new ParseRuleStateException("Legacy parse-rule definition is missing.");
                    if (reader.GetInt32(0) != modelId ||
                        !string.Equals(reader.GetString(1), targetModelType, StringComparison.Ordinal) ||
                        !string.Equals(reader.GetString(2), extension, StringComparison.Ordinal))
                        throw new ParseRuleStateException("Published legacy script model and extension are immutable; create a new script instead.");
                }
            }
        }

        private static long InsertLegacyScript(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacyScriptSaveRequest request,
            string extension)
        {
            using (var command = CreateCommand(connection, transaction, @"
INSERT INTO ParseScripts (Name, ModelId, FileExtension, ScriptCode, IsEnabled)
VALUES (@Name, @ModelId, @Extension, @ScriptCode, @IsEnabled);"))
            {
                AddLegacyParameters(command, request, extension);
                command.ExecuteNonQuery();
            }
            return Convert.ToInt64(ExecuteScalar(connection, transaction, "SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
        }

        private static void UpdateLegacyScript(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacyScriptSaveRequest request,
            string extension)
        {
            using (var command = CreateCommand(connection, transaction, @"
UPDATE ParseScripts
SET Name=@Name, ModelId=@ModelId, FileExtension=@Extension, ScriptCode=@ScriptCode,
    IsEnabled=@IsEnabled, UpdateTime=CURRENT_TIMESTAMP
WHERE Id=@LegacyScriptId;"))
            {
                AddLegacyParameters(command, request, extension);
                command.Parameters.AddWithValue("@LegacyScriptId", request.LegacyScriptId);
                if (command.ExecuteNonQuery() != 1) throw new ParseRuleStateException("Legacy script does not exist.");
            }
        }

        private static void AddLegacyParameters(SQLiteCommand command, LegacyScriptSaveRequest request, string extension)
        {
            command.Parameters.AddWithValue("@Name", request.Name.Trim());
            command.Parameters.AddWithValue("@ModelId", request.ModelId);
            command.Parameters.AddWithValue("@Extension", extension);
            command.Parameters.AddWithValue("@ScriptCode", request.ScriptCode);
            command.Parameters.AddWithValue("@IsEnabled", request.IsEnabled ? 1 : 0);
        }

        private static long InsertDefinition(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long legacyScriptId,
            LegacyScriptSaveRequest request,
            string extension,
            DateTime now)
        {
            using (var command = CreateCommand(connection, transaction, @"
INSERT INTO ParseRuleDefinitions
    (RuleName, ModelId, TargetModelType, NormalizedExtension, LegacyScriptId, CreatedTime, UpdatedTime)
VALUES
    (@Name, @ModelId, @TargetModelType, @Extension, @LegacyScriptId, @Now, @Now);"))
            {
                command.Parameters.AddWithValue("@Name", request.Name.Trim());
                command.Parameters.AddWithValue("@ModelId", request.ModelId);
                command.Parameters.AddWithValue("@TargetModelType", request.TargetModelType);
                command.Parameters.AddWithValue("@Extension", extension);
                command.Parameters.AddWithValue("@LegacyScriptId", legacyScriptId);
                command.Parameters.AddWithValue("@Now", now);
                command.ExecuteNonQuery();
            }
            return Convert.ToInt64(ExecuteScalar(connection, transaction, "SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
        }

        private static void UpdateDefinitionName(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId,
            string name,
            DateTime now)
        {
            using (var command = CreateCommand(connection, transaction, @"
UPDATE ParseRuleDefinitions SET RuleName=@Name, UpdatedTime=@Now WHERE Id=@DefinitionId;"))
            {
                command.Parameters.AddWithValue("@Name", name.Trim());
                command.Parameters.AddWithValue("@Now", now);
                command.Parameters.AddWithValue("@DefinitionId", definitionId);
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureBindingAvailable(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId,
            int machineId,
            string extension)
        {
            using (var command = CreateCommand(connection, transaction, @"
SELECT v.DefinitionId
FROM PublishedParseRuleBindings b
INNER JOIN ParseRuleVersions v ON v.Id=b.ParseRuleVersionId
WHERE b.MachineId=@MachineId AND b.NormalizedExtension=@Extension;"))
            {
                command.Parameters.AddWithValue("@MachineId", machineId.ToString(CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@Extension", extension);
                object value = command.ExecuteScalar();
                if (value != null && value != DBNull.Value && Convert.ToInt64(value, CultureInfo.InvariantCulture) != definitionId)
                    throw new ParseRuleBindingConflictException("Another parse rule is already published for this machine and extension.");
            }
        }

        private static void ReplaceLegacyMachineRows(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long scriptId,
            IEnumerable<int> machines)
        {
            using (var delete = CreateCommand(connection, transaction, "DELETE FROM ScriptMachines WHERE ScriptId=@ScriptId;"))
            {
                delete.Parameters.AddWithValue("@ScriptId", scriptId);
                delete.ExecuteNonQuery();
            }
            foreach (int machineId in machines)
            {
                using (var insert = CreateCommand(connection, transaction, "INSERT INTO ScriptMachines (ScriptId, MachineId) VALUES (@ScriptId,@MachineId);"))
                {
                    insert.Parameters.AddWithValue("@ScriptId", scriptId);
                    insert.Parameters.AddWithValue("@MachineId", machineId);
                    insert.ExecuteNonQuery();
                }
            }
        }

        private static void LoadNextVersionNumbers(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId,
            out int versionNumber,
            out int revision)
        {
            using (var command = CreateCommand(connection, transaction, @"
SELECT COALESCE(MAX(VersionNumber),0), COALESCE(MAX(Revision),0)
FROM ParseRuleVersions WHERE DefinitionId=@DefinitionId;"))
            {
                command.Parameters.AddWithValue("@DefinitionId", definitionId);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    reader.Read();
                    versionNumber = reader.GetInt32(0) + 1;
                    revision = reader.GetInt32(1) + 1;
                }
            }
        }

        private static long InsertVersion(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId,
            int versionNumber,
            int revision,
            long legacyScriptId,
            string scriptCode,
            string modelHash,
            bool publish,
            DateTime now)
        {
            using (var command = CreateCommand(connection, transaction, @"
INSERT INTO ParseRuleVersions
    (DefinitionId,VersionNumber,Revision,RuleType,Status,DefinitionJson,DerivedScriptCode,
     ContentSha256,ModelSchemaHash,ValidationSummary,CreatedTime,ValidatedTime,PublishedTime)
VALUES
    (@DefinitionId,@VersionNumber,@Revision,@RuleType,@Status,@DefinitionJson,@ScriptCode,
     @ContentSha256,@ModelSchemaHash,@ValidationSummary,@Now,NULL,@PublishedTime);"))
            {
                command.Parameters.AddWithValue("@DefinitionId", definitionId);
                command.Parameters.AddWithValue("@VersionNumber", versionNumber);
                command.Parameters.AddWithValue("@Revision", revision);
                command.Parameters.AddWithValue("@RuleType", (int)ParseRuleType.LegacyCode);
                command.Parameters.AddWithValue("@Status", (int)(publish ? ParseRuleStatus.Published : ParseRuleStatus.Draft));
                command.Parameters.AddWithValue("@DefinitionJson", JsonConvert.SerializeObject(new { legacyParseScriptId = legacyScriptId }));
                command.Parameters.AddWithValue("@ScriptCode", scriptCode);
                command.Parameters.AddWithValue("@ContentSha256", MappingRuleSerializer.Sha256(scriptCode));
                command.Parameters.AddWithValue("@ModelSchemaHash", modelHash);
                command.Parameters.AddWithValue("@ValidationSummary", "{\"source\":\"legacy-editor\",\"validated\":false}");
                command.Parameters.AddWithValue("@Now", now);
                command.Parameters.AddWithValue("@PublishedTime", publish ? (object)now : DBNull.Value);
                command.ExecuteNonQuery();
            }
            return Convert.ToInt64(ExecuteScalar(connection, transaction, "SELECT last_insert_rowid();"), CultureInfo.InvariantCulture);
        }

        private static void RemoveDefinitionBindings(SQLiteConnection connection, SQLiteTransaction transaction, long definitionId)
        {
            using (var command = CreateCommand(connection, transaction, @"
DELETE FROM PublishedParseRuleBindings
WHERE ParseRuleVersionId IN (SELECT Id FROM ParseRuleVersions WHERE DefinitionId=@DefinitionId);"))
            {
                command.Parameters.AddWithValue("@DefinitionId", definitionId);
                command.ExecuteNonQuery();
            }
        }

        private static void SupersedeOldVersions(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId,
            long newVersionId)
        {
            using (var command = CreateCommand(connection, transaction, @"
UPDATE ParseRuleVersions
SET Status=@Superseded, Revision=Revision+1
WHERE DefinitionId=@DefinitionId AND Id<>@NewVersionId AND Status=@Published;"))
            {
                command.Parameters.AddWithValue("@Superseded", (int)ParseRuleStatus.Superseded);
                command.Parameters.AddWithValue("@DefinitionId", definitionId);
                command.Parameters.AddWithValue("@NewVersionId", newVersionId);
                command.Parameters.AddWithValue("@Published", (int)ParseRuleStatus.Published);
                command.ExecuteNonQuery();
            }
        }

        private static void InsertBinding(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int machineId,
            string extension,
            long versionId,
            DateTime now)
        {
            using (var command = CreateCommand(connection, transaction, @"
INSERT INTO PublishedParseRuleBindings (MachineId,NormalizedExtension,ParseRuleVersionId,PublishedTime)
VALUES (@MachineId,@Extension,@VersionId,@Now);"))
            {
                command.Parameters.AddWithValue("@MachineId", machineId.ToString(CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@Extension", extension);
                command.Parameters.AddWithValue("@VersionId", versionId);
                command.Parameters.AddWithValue("@Now", now);
                command.ExecuteNonQuery();
            }
        }

        private static SQLiteCommand CreateCommand(SQLiteConnection connection, SQLiteTransaction transaction, string sql)
        {
            var command = new SQLiteCommand(sql, connection, transaction);
            command.CommandTimeout = 5;
            return command;
        }

        private static object ExecuteScalar(SQLiteConnection connection, SQLiteTransaction transaction, string sql)
        {
            using (var command = CreateCommand(connection, transaction, sql))
                return command.ExecuteScalar();
        }
    }
}
