using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    /// <summary>
    /// Persists immutable parse-rule versions and the single published binding for a
    /// machine and normalized file extension.
    /// </summary>
    public sealed class ParseRuleStore : IDisposable
    {
        private const int CurrentSchemaVersion = 1;
        private const int BusyTimeoutMilliseconds = 5000;

        private readonly string _databasePath;
        private readonly string _connectionString;
        private bool _initialized;
        private bool _disposed;

        public ParseRuleStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("Database path is required.", "databasePath");
            }

            _databasePath = Path.GetFullPath(databasePath);
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Version = 3,
                ForeignKeys = true,
                BusyTimeout = BusyTimeoutMilliseconds,
                Pooling = false
            };
            _connectionString = builder.ConnectionString;
        }

        /// <summary>
        /// Creates or transactionally upgrades the parse-rule schema. Repeated calls are safe.
        /// </summary>
        public void Initialize()
        {
            ThrowIfDisposed();

            string directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                int databaseVersion = Convert.ToInt32(
                    ExecuteScalar(connection, transaction, "PRAGMA user_version;"),
                    CultureInfo.InvariantCulture);

                if (databaseVersion > CurrentSchemaVersion)
                {
                    throw new ParseRuleStateException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "The parse-rule database schema version {0} is newer than supported version {1}.",
                            databaseVersion,
                            CurrentSchemaVersion));
                }

                ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS ParseRuleDefinitions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RuleName TEXT NOT NULL,
    ModelId INTEGER NOT NULL,
    TargetModelType TEXT NOT NULL,
    NormalizedExtension TEXT NOT NULL,
    CreatedTime DATETIME NOT NULL,
    UpdatedTime DATETIME NOT NULL
);

CREATE TABLE IF NOT EXISTS ParseRuleVersions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DefinitionId INTEGER NOT NULL,
    VersionNumber INTEGER NOT NULL,
    Revision INTEGER NOT NULL,
    RuleType INTEGER NOT NULL,
    Status INTEGER NOT NULL,
    DefinitionJson TEXT NOT NULL,
    DerivedScriptCode TEXT NOT NULL DEFAULT '',
    ContentSha256 TEXT NOT NULL,
    ModelSchemaHash TEXT NOT NULL,
    ValidationSummary TEXT,
    CreatedTime DATETIME NOT NULL,
    ValidatedTime DATETIME,
    PublishedTime DATETIME,
    CONSTRAINT FK_ParseRuleVersions_Definition
        FOREIGN KEY (DefinitionId) REFERENCES ParseRuleDefinitions(Id) ON DELETE RESTRICT,
    CONSTRAINT UQ_ParseRuleVersions_Number UNIQUE (DefinitionId, VersionNumber),
    CONSTRAINT CK_ParseRuleVersions_Revision CHECK (Revision > 0),
    CONSTRAINT CK_ParseRuleVersions_Type CHECK (RuleType IN (0, 1)),
    CONSTRAINT CK_ParseRuleVersions_Status CHECK (Status IN (0, 1, 2, 3))
);

CREATE TABLE IF NOT EXISTS PublishedParseRuleBindings (
    MachineId TEXT NOT NULL,
    NormalizedExtension TEXT NOT NULL,
    ParseRuleVersionId INTEGER NOT NULL,
    PublishedTime DATETIME NOT NULL,
    PRIMARY KEY (MachineId, NormalizedExtension),
    CONSTRAINT FK_PublishedParseRuleBindings_Version
        FOREIGN KEY (ParseRuleVersionId) REFERENCES ParseRuleVersions(Id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS IX_ParseRuleDefinitions_ModelId
    ON ParseRuleDefinitions(ModelId);
CREATE INDEX IF NOT EXISTS IX_ParseRuleVersions_DefinitionId
    ON ParseRuleVersions(DefinitionId, VersionNumber);
CREATE INDEX IF NOT EXISTS IX_PublishedParseRuleBindings_VersionId
    ON PublishedParseRuleBindings(ParseRuleVersionId);
");

                AddParserVersionColumnIfNeeded(connection, transaction);
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "PRAGMA user_version = " + CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture) + ";");

                transaction.Commit();
            }

            _initialized = true;
        }

        public ParseRuleVersion SaveDraft(MappingRuleDefinition definition, int expectedRevision = 0)
        {
            EnsureReady();
            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }
            if (definition.DefinitionId < 0)
            {
                throw new MappingValidationException("DefinitionId cannot be negative.");
            }
            MappingRuleSerializer.ValidateDefinition(definition);

            string normalizedExtension = MappingRuleSerializer.NormalizeExtension(definition.NormalizedExtension);
            string ruleName = definition.RuleName.Trim();
            string targetModelType = definition.TargetModelType.Trim();
            string modelSchemaHash = definition.ModelSchemaHash.Trim();
            DateTime now = DateTime.UtcNow;

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                long definitionId;
                int versionNumber;
                int revision;

                if (definition.DefinitionId == 0)
                {
                    if (expectedRevision != 0)
                    {
                        throw new ParseRuleRevisionConflictException(
                            "A new parse-rule definition must use expected revision 0.");
                    }

                    definitionId = InsertDefinition(
                        connection,
                        transaction,
                        ruleName,
                        definition.ModelId,
                        targetModelType,
                        normalizedExtension,
                        now);
                    versionNumber = 1;
                    revision = 1;
                }
                else
                {
                    definitionId = definition.DefinitionId;
                    DefinitionHead head = LoadDefinitionHead(connection, transaction, definitionId);
                    if (head == null)
                    {
                        throw new ParseRuleStateException("The parse-rule definition does not exist.");
                    }

                    if (head.LatestRevision != expectedRevision)
                    {
                        throw new ParseRuleRevisionConflictException(
                            "The parse-rule definition was changed by another operation.");
                    }

                    if (head.ModelId != definition.ModelId ||
                        !string.Equals(head.TargetModelType, targetModelType, StringComparison.Ordinal) ||
                        !string.Equals(head.NormalizedExtension, normalizedExtension, StringComparison.Ordinal))
                    {
                        throw new MappingValidationException(
                            "Model and extension cannot be changed within an existing parse-rule definition.");
                    }

                    UpdateDefinitionName(connection, transaction, definitionId, ruleName, now);
                    versionNumber = head.LatestVersionNumber + 1;
                    revision = head.LatestRevision + 1;
                }

                definition.DefinitionId = definitionId;
                definition.RuleName = ruleName;
                definition.TargetModelType = targetModelType;
                definition.ModelSchemaHash = modelSchemaHash;
                definition.NormalizedExtension = normalizedExtension;
                EnsureModelSchemaCurrent(connection, transaction, definition.ModelId, modelSchemaHash);
                EnsureMappingTargetsCurrent(connection, transaction, definition, false);

                string definitionJson = MappingRuleSerializer.Serialize(definition);
                string derivedScriptCode = new MappingScriptGenerator().Generate(definition);
                string contentSha256 = MappingRuleSerializer.Sha256(definitionJson);
                long versionId = InsertVersion(
                    connection,
                    transaction,
                    definitionId,
                    versionNumber,
                    revision,
                    ParseRuleType.Mapping,
                    ParseRuleStatus.Draft,
                    definitionJson,
                    derivedScriptCode,
                    contentSha256,
                    modelSchemaHash,
                    null,
                    now,
                    null,
                    null);

                ParseRuleVersion result = LoadVersion(connection, transaction, versionId);
                transaction.Commit();
                return result;
            }
        }

        public ParseRuleVersion Validate(long versionId, int expectedRevision, string validationSummary)
        {
            EnsureReady();
            if (versionId <= 0)
            {
                throw new ArgumentOutOfRangeException("versionId");
            }
            if (string.IsNullOrWhiteSpace(validationSummary))
            {
                throw new MappingValidationException("A validation summary is required.");
            }

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                ParseRuleVersion version = LoadVersion(connection, transaction, versionId);
                EnsureVersionExists(version);
                EnsureExpectedRevision(version, expectedRevision);
                EnsureVersionIsDefinitionHead(connection, transaction, version);
                ParseRuleIntegrityValidator.Validate(version);
                EnsureModelSchemaCurrent(connection, transaction, version.ModelId, version.ModelSchemaHash);
                EnsureVersionMappingTargetsCurrent(connection, transaction, version);

                if (version.Status != ParseRuleStatus.Draft)
                {
                    throw new ParseRuleStateException("Only a draft parse-rule version can be validated.");
                }

                DateTime now = DateTime.UtcNow;
                using (SQLiteCommand command = CreateCommand(connection, transaction, @"
UPDATE ParseRuleVersions
SET Status = @Status,
    Revision = Revision + 1,
    ValidationSummary = @ValidationSummary,
    ValidatedTime = @ValidatedTime
WHERE Id = @Id AND Revision = @ExpectedRevision AND Status = @ExpectedStatus;"))
                {
                    AddParameter(command, "@Status", DbType.Int32, (int)ParseRuleStatus.Validated);
                    AddParameter(command, "@ValidationSummary", DbType.String, validationSummary.Trim());
                    AddParameter(command, "@ValidatedTime", DbType.DateTime, now);
                    AddParameter(command, "@Id", DbType.Int64, versionId);
                    AddParameter(command, "@ExpectedRevision", DbType.Int32, expectedRevision);
                    AddParameter(command, "@ExpectedStatus", DbType.Int32, (int)ParseRuleStatus.Draft);
                    if (command.ExecuteNonQuery() != 1)
                    {
                        throw new ParseRuleRevisionConflictException(
                            "The parse-rule version was changed by another operation.");
                    }
                }

                ParseRuleVersion result = LoadVersion(connection, transaction, versionId);
                transaction.Commit();
                return result;
            }
        }

        public ParseRuleVersion Publish(
            long versionId,
            string machineId,
            int expectedRevision,
            bool replaceExisting = false)
        {
            return Publish(
                versionId,
                new[] { machineId },
                expectedRevision,
                replaceExisting);
        }

        public ParseRuleVersion Publish(
            long versionId,
            IReadOnlyCollection<string> machineIds,
            int expectedRevision,
            bool replaceExisting = false,
            IReadOnlyDictionary<string, long?> expectedExistingBindings = null)
        {
            EnsureReady();
            if (machineIds == null || machineIds.Count == 0)
            {
                throw new ArgumentException("At least one machine is required for publication.", "machineIds");
            }

            string[] normalizedMachineIds = machineIds
                .Select(NormalizeMachineId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Dictionary<string, long?> normalizedExpectations = null;
            if (expectedExistingBindings != null)
            {
                normalizedExpectations = new Dictionary<string, long?>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, long?> expectation in expectedExistingBindings)
                {
                    string normalizedMachineId = NormalizeMachineId(expectation.Key);
                    if (normalizedExpectations.ContainsKey(normalizedMachineId))
                        throw new ArgumentException("Duplicate expected machine binding.", "expectedExistingBindings");
                    normalizedExpectations.Add(normalizedMachineId, expectation.Value);
                }
                if (normalizedExpectations.Count != normalizedMachineIds.Length ||
                    normalizedMachineIds.Any(machineId => !normalizedExpectations.ContainsKey(machineId)))
                    throw new ArgumentException(
                        "Expected bindings must contain exactly the machines being published.",
                        "expectedExistingBindings");
            }

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                ParseRuleVersion version = LoadVersion(connection, transaction, versionId);
                EnsureVersionExists(version);
                EnsureExpectedRevision(version, expectedRevision);
                EnsureVersionIsDefinitionHead(connection, transaction, version);
                ParseRuleIntegrityValidator.Validate(version);
                EnsureModelSchemaCurrent(connection, transaction, version.ModelId, version.ModelSchemaHash);
                EnsureVersionMappingTargetsCurrent(connection, transaction, version);

                if (version.Status != ParseRuleStatus.Validated &&
                    version.Status != ParseRuleStatus.Published)
                {
                    throw new ParseRuleStateException(
                        "Only a validated or already-published parse-rule version can be published.");
                }

                var existingBindings = new Dictionary<string, long?>(StringComparer.Ordinal);
                foreach (string normalizedMachineId in normalizedMachineIds)
                {
                    long? existingVersionId = LoadBindingVersionId(
                        connection,
                        transaction,
                        normalizedMachineId,
                        version.NormalizedExtension);
                    if (normalizedExpectations != null)
                    {
                        long? expectedExistingVersionId = normalizedExpectations[normalizedMachineId];
                        if (existingVersionId != expectedExistingVersionId)
                            throw new ParseRuleBindingConflictException(
                                "The published binding for machine '" + normalizedMachineId +
                                "' changed after the confirmation dialog was opened.");
                    }
                    existingBindings.Add(normalizedMachineId, existingVersionId);
                    if (existingVersionId.HasValue &&
                        existingVersionId.Value != versionId &&
                        !replaceExisting)
                    {
                        throw new ParseRuleBindingConflictException(
                            "A parse rule is already published for machine '" +
                            normalizedMachineId + "' and file extension '" +
                            version.NormalizedExtension + "'.");
                    }
                }

                bool hasBindingChange = existingBindings.Any(binding =>
                    !binding.Value.HasValue || binding.Value.Value != versionId);
                if (!hasBindingChange)
                {
                    if (version.Status != ParseRuleStatus.Published)
                    {
                        throw new ParseRuleStateException(
                            "The published binding points to a version that is not published.");
                    }

                    transaction.Commit();
                    return version;
                }

                DateTime now = DateTime.UtcNow;
                var replacedVersionIds = new HashSet<long>();
                foreach (KeyValuePair<string, long?> binding in existingBindings)
                {
                    string normalizedMachineId = binding.Key;
                    long? existingVersionId = binding.Value;
                    if (existingVersionId.HasValue && existingVersionId.Value == versionId)
                    {
                        continue;
                    }

                    if (existingVersionId.HasValue)
                    {
                        using (SQLiteCommand command = CreateCommand(connection, transaction, @"
UPDATE PublishedParseRuleBindings
SET ParseRuleVersionId = @VersionId, PublishedTime = @PublishedTime
WHERE MachineId = @MachineId AND NormalizedExtension = @NormalizedExtension;"))
                        {
                            AddParameter(command, "@VersionId", DbType.Int64, versionId);
                            AddParameter(command, "@PublishedTime", DbType.DateTime, now);
                            AddParameter(command, "@MachineId", DbType.String, normalizedMachineId);
                            AddParameter(command, "@NormalizedExtension", DbType.String, version.NormalizedExtension);
                            if (command.ExecuteNonQuery() != 1)
                            {
                                throw new ParseRuleBindingConflictException(
                                    "The published parse-rule binding changed during publication.");
                            }
                        }
                        replacedVersionIds.Add(existingVersionId.Value);
                    }
                    else
                    {
                        using (SQLiteCommand command = CreateCommand(connection, transaction, @"
INSERT INTO PublishedParseRuleBindings
    (MachineId, NormalizedExtension, ParseRuleVersionId, PublishedTime)
VALUES
    (@MachineId, @NormalizedExtension, @VersionId, @PublishedTime);"))
                        {
                            AddParameter(command, "@MachineId", DbType.String, normalizedMachineId);
                            AddParameter(command, "@NormalizedExtension", DbType.String, version.NormalizedExtension);
                            AddParameter(command, "@VersionId", DbType.Int64, versionId);
                            AddParameter(command, "@PublishedTime", DbType.DateTime, now);
                            command.ExecuteNonQuery();
                        }
                    }
                }

                using (SQLiteCommand command = CreateCommand(connection, transaction, @"
UPDATE ParseRuleVersions
SET Status = @Status,
    Revision = Revision + 1,
    PublishedTime = COALESCE(PublishedTime, @PublishedTime)
WHERE Id = @Id AND Revision = @ExpectedRevision AND Status IN (@ValidatedStatus, @PublishedStatus);"))
                {
                    AddParameter(command, "@Status", DbType.Int32, (int)ParseRuleStatus.Published);
                    AddParameter(command, "@PublishedTime", DbType.DateTime, now);
                    AddParameter(command, "@Id", DbType.Int64, versionId);
                    AddParameter(command, "@ExpectedRevision", DbType.Int32, expectedRevision);
                    AddParameter(command, "@ValidatedStatus", DbType.Int32, (int)ParseRuleStatus.Validated);
                    AddParameter(command, "@PublishedStatus", DbType.Int32, (int)ParseRuleStatus.Published);
                    if (command.ExecuteNonQuery() != 1)
                    {
                        throw new ParseRuleRevisionConflictException(
                            "The parse-rule version was changed by another operation.");
                    }
                }

                foreach (long replacedVersionId in replacedVersionIds)
                {
                    SupersedeIfUnbound(connection, transaction, replacedVersionId);
                }

                TouchDefinition(connection, transaction, version.DefinitionId, now);
                ParseRuleVersion result = LoadVersion(connection, transaction, versionId);
                transaction.Commit();
                return result;
            }
        }

        public ParseRuleVersion GetPublished(string machineId, string normalizedExtension)
        {
            EnsureReady();
            string normalizedMachineId = NormalizeMachineId(machineId);
            string extension = MappingRuleSerializer.NormalizeExtension(normalizedExtension);

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = CreateCommand(connection, null, VersionSelectSql + @"
INNER JOIN PublishedParseRuleBindings b ON b.ParseRuleVersionId = v.Id
WHERE b.MachineId = @MachineId AND b.NormalizedExtension = @NormalizedExtension;"))
            {
                AddParameter(command, "@MachineId", DbType.String, normalizedMachineId);
                AddParameter(command, "@NormalizedExtension", DbType.String, extension);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    ParseRuleVersion result = ReadVersion(reader);
                    if (result.Status != ParseRuleStatus.Published)
                    {
                        throw new ParseRuleStateException(
                            "The published binding points to a version that is not published.");
                    }
                    ParseRuleIntegrityValidator.Validate(result);
                    return result;
                }
            }
        }

        public ParseRuleVersion Rollback(
            long historicalVersionId,
            string machineId,
            long expectedCurrentVersionId)
        {
            EnsureReady();
            if (historicalVersionId <= 0)
            {
                throw new ArgumentOutOfRangeException("historicalVersionId");
            }
            if (expectedCurrentVersionId <= 0)
            {
                throw new ArgumentOutOfRangeException("expectedCurrentVersionId");
            }
            string normalizedMachineId = NormalizeMachineId(machineId);

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                ParseRuleVersion historical = LoadVersion(connection, transaction, historicalVersionId);
                EnsureVersionExists(historical);
                ParseRuleIntegrityValidator.Validate(historical);
                EnsureModelSchemaCurrent(connection, transaction, historical.ModelId, historical.ModelSchemaHash);
                EnsureVersionMappingTargetsCurrent(connection, transaction, historical);
                if (historical.Status != ParseRuleStatus.Published &&
                    historical.Status != ParseRuleStatus.Superseded)
                {
                    throw new ParseRuleStateException("Only a previously published version can be rolled back.");
                }

                long? currentVersionId = LoadBindingVersionId(
                    connection,
                    transaction,
                    normalizedMachineId,
                    historical.NormalizedExtension);
                if (!currentVersionId.HasValue)
                {
                    throw new ParseRuleStateException(
                        "There is no published parse rule to replace for this machine and extension.");
                }
                if (currentVersionId.Value != expectedCurrentVersionId)
                {
                    throw new ParseRuleBindingConflictException(
                        "The published parse-rule binding changed after rollback confirmation.");
                }
                if (currentVersionId.Value == historicalVersionId)
                {
                    throw new ParseRuleStateException("The selected historical version is already published.");
                }

                ParseRuleVersion current = LoadVersion(connection, transaction, currentVersionId.Value);
                EnsureVersionExists(current);
                ParseRuleIntegrityValidator.Validate(current);
                if (current.DefinitionId != historical.DefinitionId)
                {
                    throw new ParseRuleStateException(
                        "Rollback is only allowed within the currently published parse-rule definition.");
                }
                if (!string.Equals(
                    current.ModelSchemaHash,
                    historical.ModelSchemaHash,
                    StringComparison.Ordinal))
                {
                    throw new ParseRuleStateException(
                        "The historical version model schema does not match the current published version.");
                }

                DefinitionHead head = LoadDefinitionHead(connection, transaction, historical.DefinitionId);
                if (head == null)
                {
                    throw new ParseRuleStateException("The parse-rule definition does not exist.");
                }

                DateTime now = DateTime.UtcNow;
                long newVersionId = InsertVersion(
                    connection,
                    transaction,
                    historical.DefinitionId,
                    head.LatestVersionNumber + 1,
                    head.LatestRevision + 1,
                    historical.RuleType,
                    ParseRuleStatus.Published,
                    historical.DefinitionJson,
                    historical.DerivedScriptCode ?? string.Empty,
                    historical.ContentSha256,
                    historical.ModelSchemaHash,
                    historical.ValidationSummary,
                    now,
                    now,
                    now);

                using (SQLiteCommand command = CreateCommand(connection, transaction, @"
UPDATE PublishedParseRuleBindings
SET ParseRuleVersionId = @NewVersionId, PublishedTime = @PublishedTime
WHERE MachineId = @MachineId
  AND NormalizedExtension = @NormalizedExtension
  AND ParseRuleVersionId = @ExpectedCurrentVersionId;"))
                {
                    AddParameter(command, "@NewVersionId", DbType.Int64, newVersionId);
                    AddParameter(command, "@PublishedTime", DbType.DateTime, now);
                    AddParameter(command, "@MachineId", DbType.String, normalizedMachineId);
                    AddParameter(command, "@NormalizedExtension", DbType.String, historical.NormalizedExtension);
                    AddParameter(command, "@ExpectedCurrentVersionId", DbType.Int64, expectedCurrentVersionId);
                    if (command.ExecuteNonQuery() != 1)
                    {
                        throw new ParseRuleBindingConflictException(
                            "The published parse-rule binding changed during rollback.");
                    }
                }

                SupersedeIfUnbound(connection, transaction, currentVersionId.Value);
                TouchDefinition(connection, transaction, historical.DefinitionId, now);
                ParseRuleVersion result = LoadVersion(connection, transaction, newVersionId);
                transaction.Commit();
                return result;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _initialized = false;
        }

        private static readonly string VersionSelectSql = @"
SELECT
    v.Id,
    v.DefinitionId,
    v.VersionNumber,
    v.Revision,
    v.RuleType,
    v.Status,
    d.RuleName,
    d.ModelId,
    d.TargetModelType,
    d.NormalizedExtension,
    v.DefinitionJson,
    v.DerivedScriptCode,
    v.ContentSha256,
    v.ModelSchemaHash,
    v.ValidationSummary,
    v.CreatedTime,
    v.ValidatedTime,
    v.PublishedTime
FROM ParseRuleVersions v
INNER JOIN ParseRuleDefinitions d ON d.Id = v.DefinitionId
";

        private SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            try
            {
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = " +
                        BusyTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture) +
                        ";";
                    command.ExecuteNonQuery();
                }
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static void AddParserVersionColumnIfNeeded(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            if (!TableExists(connection, transaction, "FileProcessRecord"))
            {
                return;
            }

            bool parserVersionColumnExists = false;
            using (SQLiteCommand command = CreateCommand(
                connection,
                transaction,
                "PRAGMA table_info(FileProcessRecord);"))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "ParserVersionId", StringComparison.OrdinalIgnoreCase))
                    {
                        parserVersionColumnExists = true;
                        break;
                    }
                }
            }

            if (!parserVersionColumnExists)
            {
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "ALTER TABLE FileProcessRecord ADD COLUMN ParserVersionId INTEGER NULL;");
            }

            ExecuteNonQuery(
                connection,
                transaction,
                "CREATE INDEX IF NOT EXISTS IX_FileProcessRecord_ParserVersionId " +
                "ON FileProcessRecord(ParserVersionId);");
        }

        private static bool TableExists(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string tableName)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT COUNT(*)
FROM sqlite_master
WHERE type = 'table' AND name = @TableName;"))
            {
                AddParameter(command, "@TableName", DbType.String, tableName);
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
            }
        }

        private static long InsertDefinition(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string ruleName,
            int modelId,
            string targetModelType,
            string normalizedExtension,
            DateTime now)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
INSERT INTO ParseRuleDefinitions
    (RuleName, ModelId, TargetModelType, NormalizedExtension, CreatedTime, UpdatedTime)
VALUES
    (@RuleName, @ModelId, @TargetModelType, @NormalizedExtension, @CreatedTime, @UpdatedTime);"))
            {
                AddParameter(command, "@RuleName", DbType.String, ruleName);
                AddParameter(command, "@ModelId", DbType.Int32, modelId);
                AddParameter(command, "@TargetModelType", DbType.String, targetModelType);
                AddParameter(command, "@NormalizedExtension", DbType.String, normalizedExtension);
                AddParameter(command, "@CreatedTime", DbType.DateTime, now);
                AddParameter(command, "@UpdatedTime", DbType.DateTime, now);
                command.ExecuteNonQuery();
            }

            return Convert.ToInt64(
                ExecuteScalar(connection, transaction, "SELECT last_insert_rowid();"),
                CultureInfo.InvariantCulture);
        }

        private static long InsertVersion(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId,
            int versionNumber,
            int revision,
            ParseRuleType ruleType,
            ParseRuleStatus status,
            string definitionJson,
            string derivedScriptCode,
            string contentSha256,
            string modelSchemaHash,
            string validationSummary,
            DateTime createdTime,
            DateTime? validatedTime,
            DateTime? publishedTime)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
INSERT INTO ParseRuleVersions
    (DefinitionId, VersionNumber, Revision, RuleType, Status, DefinitionJson,
     DerivedScriptCode, ContentSha256, ModelSchemaHash, ValidationSummary,
     CreatedTime, ValidatedTime, PublishedTime)
VALUES
    (@DefinitionId, @VersionNumber, @Revision, @RuleType, @Status, @DefinitionJson,
     @DerivedScriptCode, @ContentSha256, @ModelSchemaHash, @ValidationSummary,
     @CreatedTime, @ValidatedTime, @PublishedTime);"))
            {
                AddParameter(command, "@DefinitionId", DbType.Int64, definitionId);
                AddParameter(command, "@VersionNumber", DbType.Int32, versionNumber);
                AddParameter(command, "@Revision", DbType.Int32, revision);
                AddParameter(command, "@RuleType", DbType.Int32, (int)ruleType);
                AddParameter(command, "@Status", DbType.Int32, (int)status);
                AddParameter(command, "@DefinitionJson", DbType.String, definitionJson);
                AddParameter(command, "@DerivedScriptCode", DbType.String, derivedScriptCode ?? string.Empty);
                AddParameter(command, "@ContentSha256", DbType.String, contentSha256);
                AddParameter(command, "@ModelSchemaHash", DbType.String, modelSchemaHash);
                AddParameter(command, "@ValidationSummary", DbType.String, validationSummary);
                AddParameter(command, "@CreatedTime", DbType.DateTime, createdTime);
                AddParameter(command, "@ValidatedTime", DbType.DateTime, validatedTime);
                AddParameter(command, "@PublishedTime", DbType.DateTime, publishedTime);
                command.ExecuteNonQuery();
            }

            return Convert.ToInt64(
                ExecuteScalar(connection, transaction, "SELECT last_insert_rowid();"),
                CultureInfo.InvariantCulture);
        }

        private static DefinitionHead LoadDefinitionHead(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT
    d.ModelId,
    d.TargetModelType,
    d.NormalizedExtension,
    COALESCE(v.Id, 0),
    COALESCE(v.VersionNumber, 0),
    COALESCE(v.Revision, 0)
FROM ParseRuleDefinitions d
LEFT JOIN ParseRuleVersions v
       ON v.DefinitionId = d.Id
      AND v.VersionNumber = (
          SELECT MAX(v2.VersionNumber)
          FROM ParseRuleVersions v2
          WHERE v2.DefinitionId = d.Id)
WHERE d.Id = @DefinitionId;"))
            {
                AddParameter(command, "@DefinitionId", DbType.Int64, definitionId);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return new DefinitionHead
                    {
                        ModelId = reader.GetInt32(0),
                        TargetModelType = reader.GetString(1),
                        NormalizedExtension = reader.GetString(2),
                        LatestVersionId = reader.GetInt64(3),
                        LatestVersionNumber = reader.GetInt32(4),
                        LatestRevision = reader.GetInt32(5)
                    };
                }
            }
        }

        private static void UpdateDefinitionName(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId,
            string ruleName,
            DateTime now)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
UPDATE ParseRuleDefinitions
SET RuleName = @RuleName, UpdatedTime = @UpdatedTime
WHERE Id = @DefinitionId;"))
            {
                AddParameter(command, "@RuleName", DbType.String, ruleName);
                AddParameter(command, "@UpdatedTime", DbType.DateTime, now);
                AddParameter(command, "@DefinitionId", DbType.Int64, definitionId);
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new ParseRuleStateException("The parse-rule definition does not exist.");
                }
            }
        }

        private static void TouchDefinition(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long definitionId,
            DateTime now)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
UPDATE ParseRuleDefinitions
SET UpdatedTime = @UpdatedTime
WHERE Id = @DefinitionId;"))
            {
                AddParameter(command, "@UpdatedTime", DbType.DateTime, now);
                AddParameter(command, "@DefinitionId", DbType.Int64, definitionId);
                command.ExecuteNonQuery();
            }
        }

        private static long? LoadBindingVersionId(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string machineId,
            string normalizedExtension)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT ParseRuleVersionId
FROM PublishedParseRuleBindings
WHERE MachineId = @MachineId AND NormalizedExtension = @NormalizedExtension;"))
            {
                AddParameter(command, "@MachineId", DbType.String, machineId);
                AddParameter(command, "@NormalizedExtension", DbType.String, normalizedExtension);
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                {
                    return null;
                }
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        private static void SupersedeIfUnbound(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long versionId)
        {
            using (SQLiteCommand countCommand = CreateCommand(connection, transaction, @"
SELECT COUNT(*)
FROM PublishedParseRuleBindings
WHERE ParseRuleVersionId = @VersionId;"))
            {
                AddParameter(countCommand, "@VersionId", DbType.Int64, versionId);
                if (Convert.ToInt32(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                {
                    return;
                }
            }

            using (SQLiteCommand updateCommand = CreateCommand(connection, transaction, @"
UPDATE ParseRuleVersions
SET Status = @SupersededStatus, Revision = Revision + 1
WHERE Id = @VersionId AND Status = @PublishedStatus;"))
            {
                AddParameter(updateCommand, "@SupersededStatus", DbType.Int32, (int)ParseRuleStatus.Superseded);
                AddParameter(updateCommand, "@VersionId", DbType.Int64, versionId);
                AddParameter(updateCommand, "@PublishedStatus", DbType.Int32, (int)ParseRuleStatus.Published);
                updateCommand.ExecuteNonQuery();
            }
        }

        private static ParseRuleVersion LoadVersion(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long versionId)
        {
            using (SQLiteCommand command = CreateCommand(
                connection,
                transaction,
                VersionSelectSql + "WHERE v.Id = @VersionId;"))
            {
                AddParameter(command, "@VersionId", DbType.Int64, versionId);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    return reader.Read() ? ReadVersion(reader) : null;
                }
            }
        }

        private static ParseRuleVersion ReadVersion(SQLiteDataReader reader)
        {
            return new ParseRuleVersion
            {
                Id = reader.GetInt64(0),
                DefinitionId = reader.GetInt64(1),
                VersionNumber = reader.GetInt32(2),
                Revision = reader.GetInt32(3),
                RuleType = (ParseRuleType)reader.GetInt32(4),
                Status = (ParseRuleStatus)reader.GetInt32(5),
                RuleName = reader.GetString(6),
                ModelId = reader.GetInt32(7),
                TargetModelType = reader.GetString(8),
                NormalizedExtension = reader.GetString(9),
                DefinitionJson = reader.GetString(10),
                DerivedScriptCode = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                ContentSha256 = reader.GetString(12),
                ModelSchemaHash = reader.GetString(13),
                ValidationSummary = reader.IsDBNull(14) ? null : reader.GetString(14),
                CreatedTime = reader.GetDateTime(15),
                ValidatedTime = reader.IsDBNull(16) ? (DateTime?)null : reader.GetDateTime(16),
                PublishedTime = reader.IsDBNull(17) ? (DateTime?)null : reader.GetDateTime(17)
            };
        }

        private static string NormalizeMachineId(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
            {
                throw new MappingValidationException("MachineId is required.");
            }

            string normalized = machineId.Trim();
            if (normalized.Length > 128)
            {
                throw new MappingValidationException("MachineId is too long.");
            }
            for (int index = 0; index < normalized.Length; index++)
            {
                if (char.IsControl(normalized[index]))
                {
                    throw new MappingValidationException("MachineId contains a control character.");
                }
            }
            return normalized;
        }

        private static SQLiteCommand CreateCommand(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string commandText)
        {
            var command = connection.CreateCommand();
            command.CommandText = commandText;
            command.CommandTimeout = BusyTimeoutMilliseconds / 1000;
            if (transaction != null)
            {
                command.Transaction = transaction;
            }
            return command;
        }

        private static void AddParameter(
            SQLiteCommand command,
            string parameterName,
            DbType dbType,
            object value)
        {
            SQLiteParameter parameter = command.Parameters.Add(parameterName, dbType);
            parameter.Value = value ?? DBNull.Value;
        }

        private static object ExecuteScalar(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string commandText)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, commandText))
            {
                return command.ExecuteScalar();
            }
        }

        private static void ExecuteNonQuery(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string commandText)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, commandText))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureVersionExists(ParseRuleVersion version)
        {
            if (version == null)
            {
                throw new ParseRuleStateException("The parse-rule version does not exist.");
            }
        }

        private static void EnsureExpectedRevision(ParseRuleVersion version, int expectedRevision)
        {
            if (version.Revision != expectedRevision)
            {
                throw new ParseRuleRevisionConflictException(
                    "The parse-rule version was changed by another operation.");
            }
        }

        private static void EnsureVersionIsDefinitionHead(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            ParseRuleVersion version)
        {
            DefinitionHead head = LoadDefinitionHead(connection, transaction, version.DefinitionId);
            if (head == null ||
                head.LatestVersionId != version.Id ||
                head.LatestVersionNumber != version.VersionNumber ||
                head.LatestRevision != version.Revision)
                throw new ParseRuleRevisionConflictException(
                    "A newer version of this parse-rule definition exists; reload before validating or publishing.");
        }

        private static void EnsureModelSchemaCurrent(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int modelId,
            string expectedHash)
        {
            if (!TableExists(connection, transaction, "ModelFields")) return;
            var fields = new System.Collections.Generic.List<ModelSchemaField>();
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description
FROM ModelFields
WHERE ModelId = @ModelId;"))
            {
                AddParameter(command, "@ModelId", DbType.Int32, modelId);
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
            if (fields.Count == 0) return;
            string currentHash = ModelSchemaService.ComputeHash(fields);
            if (!string.Equals(currentHash, expectedHash, StringComparison.Ordinal))
                throw new ParseRuleStateException("The model schema changed; save and validate a new draft before publishing or rollback.");
        }

        private static void EnsureMappingTargetsCurrent(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            MappingRuleDefinition definition,
            bool requireAllRequiredFields)
        {
            if (TableExists(connection, transaction, "DataModels"))
            {
                using (SQLiteCommand command = CreateCommand(connection, transaction,
                    "SELECT ModelName FROM DataModels WHERE Id = @ModelId;"))
                {
                    AddParameter(command, "@ModelId", DbType.Int32, definition.ModelId);
                    object value = command.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                        throw new MappingValidationException("The target model does not exist.");
                    if (!string.Equals(
                        Convert.ToString(value, CultureInfo.InvariantCulture),
                        definition.TargetModelType,
                        StringComparison.Ordinal))
                        throw new MappingValidationException("The target model name no longer matches the selected model id.");
                }
            }

            if (!TableExists(connection, transaction, "ModelFields")) return;
            var modelFields = new Dictionary<string, ModelSchemaField>(StringComparer.Ordinal);
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT FieldName, FieldType, IsRequired
FROM ModelFields
WHERE ModelId = @ModelId;"))
            {
                AddParameter(command, "@ModelId", DbType.Int32, definition.ModelId);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string fieldName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                        if (modelFields.ContainsKey(fieldName))
                            throw new MappingValidationException("The target model contains duplicate field names: " + fieldName);
                        modelFields.Add(fieldName, new ModelSchemaField
                        {
                            FieldName = fieldName,
                            FieldType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            IsRequired = !reader.IsDBNull(2) && reader.GetInt32(2) != 0
                        });
                    }
                }
            }

            foreach (FieldMappingRule mappedField in definition.Fields)
            {
                ModelSchemaField modelField;
                if (!modelFields.TryGetValue(mappedField.TargetField, out modelField))
                    throw new MappingValidationException("The target model field does not exist: " + mappedField.TargetField);
                if (!string.Equals(
                    (modelField.FieldType ?? string.Empty).Trim(),
                    (mappedField.TargetType ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase))
                    throw new MappingValidationException("The target model field type changed: " + mappedField.TargetField);
                if (modelField.IsRequired != mappedField.IsRequired)
                    throw new MappingValidationException("The target model field required flag changed: " + mappedField.TargetField);
            }

            if (!requireAllRequiredFields) return;

            var mappedTargets = new HashSet<string>(
                definition.Fields.Select(field => field.TargetField),
                StringComparer.Ordinal);
            string[] missingRequired = modelFields.Values
                .Where(field => field.IsRequired && !mappedTargets.Contains(field.FieldName))
                .Select(field => field.FieldName)
                .OrderBy(fieldName => fieldName, StringComparer.Ordinal)
                .ToArray();
            if (missingRequired.Length > 0)
                throw new MappingValidationException(
                    "Required target model fields are not mapped: " + string.Join(", ", missingRequired));
        }

        private static void EnsureVersionMappingTargetsCurrent(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            ParseRuleVersion version)
        {
            if (version == null || version.RuleType != ParseRuleType.Mapping) return;
            MappingRuleDefinition definition = MappingRuleSerializer.Deserialize(version.DefinitionJson);
            MappingRuleSerializer.ValidateDefinition(definition);
            EnsureMappingTargetsCurrent(connection, transaction, definition, true);
        }

        private void EnsureReady()
        {
            ThrowIfDisposed();
            if (!_initialized)
            {
                throw new InvalidOperationException("Initialize must be called before using the parse-rule store.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("ParseRuleStore");
            }
        }

        private sealed class DefinitionHead
        {
            public int ModelId { get; set; }
            public string TargetModelType { get; set; }
            public string NormalizedExtension { get; set; }
            public long LatestVersionId { get; set; }
            public int LatestVersionNumber { get; set; }
            public int LatestRevision { get; set; }
        }
    }
}
