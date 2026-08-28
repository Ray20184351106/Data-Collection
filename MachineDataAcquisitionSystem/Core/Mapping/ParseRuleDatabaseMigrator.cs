using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public enum LegacyMigrationIssueSeverity
    {
        Warning = 0,
        Blocking = 1
    }

    public sealed class LegacyMigrationIssue
    {
        public string Code { get; set; }
        public LegacyMigrationIssueSeverity Severity { get; set; }
        public long? ParseScriptId { get; set; }
        public int? MachineId { get; set; }
        public long? FieldMappingId { get; set; }
        public string Message { get; set; }
    }

    public sealed class LegacyScriptCandidate
    {
        public long ParseScriptId { get; set; }
        public string ScriptName { get; set; }
        public int ModelId { get; set; }
        public string NormalizedExtension { get; set; }
    }

    public sealed class LegacyBindingConflict
    {
        public LegacyBindingConflict()
        {
            Candidates = new List<LegacyScriptCandidate>();
            CandidateScriptIds = new List<long>();
        }

        public int MachineId { get; set; }
        public string NormalizedExtension { get; set; }
        public List<LegacyScriptCandidate> Candidates { get; private set; }
        public List<long> CandidateScriptIds { get; private set; }
    }

    public sealed class LegacyBindingChoice
    {
        private long _selectedScriptId;

        public int MachineId { get; set; }
        public string NormalizedExtension { get; set; }
        public long SelectedScriptId
        {
            get { return _selectedScriptId; }
            set { _selectedScriptId = value; }
        }

        public long SelectedParseScriptId
        {
            get { return _selectedScriptId; }
            set { _selectedScriptId = value; }
        }
    }

    public sealed class LegacyMigrationReport
    {
        public LegacyMigrationReport()
        {
            Issues = new List<LegacyMigrationIssue>();
            BindingConflicts = new List<LegacyBindingConflict>();
        }

        public List<LegacyMigrationIssue> Issues { get; private set; }
        public List<LegacyBindingConflict> BindingConflicts { get; private set; }

        public List<LegacyBindingConflict> Conflicts
        {
            get { return BindingConflicts; }
        }

        public List<LegacyMigrationIssue> Warnings
        {
            get
            {
                return Issues
                    .Where(issue => issue.Severity == LegacyMigrationIssueSeverity.Warning)
                    .ToList();
            }
        }

        public bool HasBlockingIssues
        {
            get
            {
                return BindingConflicts.Count != 0 ||
                       Issues.Any(issue => issue.Severity == LegacyMigrationIssueSeverity.Blocking);
            }
        }

        public bool RequiresBindingChoices
        {
            get { return BindingConflicts.Count != 0; }
        }
    }

    public sealed class LegacyMigrationConflictException : InvalidOperationException
    {
        public LegacyMigrationConflictException(string message, LegacyMigrationReport report)
            : base(message)
        {
            Report = report;
        }

        public LegacyMigrationReport Report { get; private set; }

        public IList<LegacyBindingConflict> Conflicts
        {
            get { return Report == null ? new List<LegacyBindingConflict>() : Report.BindingConflicts; }
        }
    }

    /// <summary>
    /// Audits and imports the legacy ParseScripts/ScriptMachines configuration without
    /// ever selecting a winner for a duplicate binding implicitly.
    /// </summary>
    public sealed class ParseRuleDatabaseMigrator
    {
        private const int CompatibleSchemaVersion = 1;
        private const int BusyTimeoutMilliseconds = 5000;

        private readonly string _databasePath;
        private readonly string _generatedModelsDirectory;
        private readonly string _writeConnectionString;

        public ParseRuleDatabaseMigrator(string databasePath)
            : this(databasePath, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeneratedModels"))
        {
        }

        public ParseRuleDatabaseMigrator(string databasePath, string generatedModelsDirectory)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("Database path is required.", "databasePath");
            }
            if (string.IsNullOrWhiteSpace(generatedModelsDirectory))
            {
                throw new ArgumentException("GeneratedModels directory is required.", "generatedModelsDirectory");
            }

            _databasePath = Path.GetFullPath(databasePath);
            _generatedModelsDirectory = Path.GetFullPath(generatedModelsDirectory);
            _writeConnectionString = BuildConnectionString(_databasePath, false, false);
        }

        /// <summary>
        /// Reads the legacy database without creating the file or changing schema/data.
        /// </summary>
        public LegacyMigrationReport InspectLegacy()
        {
            if (!File.Exists(_databasePath))
            {
                return new LegacyMigrationReport();
            }

            using (SQLiteConnection connection = new SQLiteConnection(
                BuildConnectionString(_databasePath, true, true)))
            {
                connection.Open();
                ConfigureConnection(connection);
                LegacySnapshot snapshot = ReadSnapshot(connection, null);
                return snapshot.Report;
            }
        }

        public void Migrate(IReadOnlyCollection<LegacyBindingChoice> choices)
        {
            List<LegacyBindingChoice> materializedChoices = choices == null
                ? new List<LegacyBindingChoice>()
                : choices.ToList();

            // Fail obvious conflicts through a strictly read-only connection first.
            if (File.Exists(_databasePath))
            {
                using (SQLiteConnection inspectionConnection = new SQLiteConnection(
                    BuildConnectionString(_databasePath, true, true)))
                {
                    inspectionConnection.Open();
                    ConfigureConnection(inspectionConnection);
                    LegacySnapshot initialSnapshot = ReadSnapshot(inspectionConnection, null);
                    ResolveSelectedBindings(initialSnapshot, materializedChoices);
                    ThrowIfBlocked(initialSnapshot.Report);
                }
            }
            else if (materializedChoices.Count != 0)
            {
                throw new LegacyMigrationConflictException(
                    "Binding choices were supplied, but no legacy database exists.",
                    CreateChoiceIssueReport("UNEXPECTED_BINDING_CHOICE", "No legacy binding conflict exists."));
            }

            string directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (SQLiteConnection connection = new SQLiteConnection(_writeConnectionString))
            {
                connection.Open();
                ConfigureConnection(connection);
                using (SQLiteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    // Re-read under the immediate write lock to close the inspection/write race.
                    LegacySnapshot snapshot = ReadSnapshot(connection, transaction);
                    Dictionary<LegacyBindingKey, long> selectedBindings =
                        ResolveSelectedBindings(snapshot, materializedChoices);
                    AddPublishedBindingIssues(snapshot, selectedBindings);
                    ThrowIfBlocked(snapshot.Report);

                    EnsureTargetSchema(connection, transaction, snapshot.DatabaseUserVersion);
                    ImportLegacyScripts(
                        connection,
                        transaction,
                        snapshot,
                        selectedBindings);
                    transaction.Commit();
                }
            }
        }

        private LegacySnapshot ReadSnapshot(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var snapshot = new LegacySnapshot();
            snapshot.DatabaseUserVersion = Convert.ToInt32(
                ExecuteScalar(connection, transaction, "PRAGMA user_version;"),
                CultureInfo.InvariantCulture);

            if (snapshot.DatabaseUserVersion > CompatibleSchemaVersion)
            {
                AddIssue(
                    snapshot.Report,
                    "UNSUPPORTED_DATABASE_VERSION",
                    LegacyMigrationIssueSeverity.Blocking,
                    null,
                    null,
                    null,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Database user_version {0} is newer than supported version {1}.",
                        snapshot.DatabaseUserVersion,
                        CompatibleSchemaVersion));
            }

            snapshot.Tables = LoadTableNames(connection, transaction);
            bool hasAnyLegacyTable =
                snapshot.Tables.Contains("ParseScripts") ||
                snapshot.Tables.Contains("ScriptMachines") ||
                snapshot.Tables.Contains("DataModels") ||
                snapshot.Tables.Contains("ModelFields") ||
                snapshot.Tables.Contains("FieldMappings") ||
                snapshot.Tables.Contains("Machines");

            if (!hasAnyLegacyTable)
            {
                LoadExistingTargetState(connection, transaction, snapshot);
                return snapshot;
            }

            RequireLegacyTable(snapshot, "ParseScripts");
            RequireLegacyTable(snapshot, "ScriptMachines");
            RequireLegacyTable(snapshot, "DataModels");
            RequireLegacyTable(snapshot, "ModelFields");

            LoadModels(connection, transaction, snapshot);
            LoadModelFields(connection, transaction, snapshot);
            LoadMachines(connection, transaction, snapshot);
            LoadScripts(connection, transaction, snapshot);
            LoadScriptMachines(connection, transaction, snapshot);
            LoadFieldMappings(connection, transaction, snapshot);
            LoadExistingTargetState(connection, transaction, snapshot);

            AuditScripts(snapshot);
            AuditScriptMachines(snapshot);
            AuditFieldMappings(snapshot);
            BuildBindingConflicts(snapshot);
            AddPublishedBindingIssues(snapshot, SelectOnlyUnambiguousBindings(snapshot));
            return snapshot;
        }

        private static HashSet<string> LoadTableNames(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT name
FROM sqlite_master
WHERE type = 'table';"))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            return tables;
        }

        private static void RequireLegacyTable(LegacySnapshot snapshot, string tableName)
        {
            if (!snapshot.Tables.Contains(tableName))
            {
                AddIssue(
                    snapshot.Report,
                    "MISSING_LEGACY_TABLE",
                    LegacyMigrationIssueSeverity.Blocking,
                    null,
                    null,
                    null,
                    "Required legacy table is missing: " + tableName + ".");
            }
        }

        private static void LoadModels(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacySnapshot snapshot)
        {
            if (!snapshot.Tables.Contains("DataModels"))
            {
                return;
            }

            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT Id, ModelName, TableName
FROM DataModels
ORDER BY Id;"))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var model = new LegacyModelRow
                    {
                        Id = reader.GetInt32(0),
                        ModelName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        TableName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        ParentModelId = 0
                    };
                    snapshot.Models[model.Id] = model;
                }
            }
        }

        private static void LoadModelFields(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacySnapshot snapshot)
        {
            if (!snapshot.Tables.Contains("ModelFields"))
            {
                return;
            }

            string fieldLengthExpression = ColumnExists(connection, transaction, "ModelFields", "FieldLength")
                ? "COALESCE(FieldLength, 0)"
                : "0";
            string primaryKeyExpression = ColumnExists(connection, transaction, "ModelFields", "IsPrimaryKey")
                ? "COALESCE(IsPrimaryKey, 0)"
                : "0";
            string identityExpression = ColumnExists(connection, transaction, "ModelFields", "IsIdentity")
                ? "COALESCE(IsIdentity, 0)"
                : "0";
            string commandText = string.Format(
                CultureInfo.InvariantCulture,
                @"SELECT Id, ModelId, FieldName, FieldType, {0}, IsRequired, {1}, {2}
FROM ModelFields
ORDER BY ModelId, FieldName, Id;",
                fieldLengthExpression,
                primaryKeyExpression,
                identityExpression);

            using (SQLiteCommand command = CreateCommand(connection, transaction, commandText))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var field = new LegacyModelFieldRow
                    {
                        Id = reader.GetInt64(0),
                        ModelId = reader.GetInt32(1),
                        FieldName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        FieldType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        FieldLength = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                        IsRequired = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                        IsPrimaryKey = !reader.IsDBNull(6) && reader.GetInt32(6) == 1,
                        IsIdentity = !reader.IsDBNull(7) && reader.GetInt32(7) == 1
                    };
                    snapshot.ModelFields.Add(field);
                }
            }
        }

        private static void LoadMachines(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacySnapshot snapshot)
        {
            if (!snapshot.Tables.Contains("Machines"))
            {
                return;
            }

            using (SQLiteCommand command = CreateCommand(connection, transaction, "SELECT Id FROM Machines;"))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    snapshot.MachineIds.Add(reader.GetInt32(0));
                }
            }
        }

        private static void LoadScripts(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacySnapshot snapshot)
        {
            if (!snapshot.Tables.Contains("ParseScripts"))
            {
                return;
            }

            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT Id, Name, ModelId, FileExtension, ScriptCode, IsEnabled
FROM ParseScripts
ORDER BY Id;"))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var script = new LegacyScriptRow
                    {
                        Id = reader.GetInt64(0),
                        Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        ModelId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        RawExtension = reader.IsDBNull(3) ? null : reader.GetString(3),
                        ScriptCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IsEnabled = !reader.IsDBNull(5) && reader.GetInt32(5) == 1,
                        CreateTime = null,
                        UpdateTime = null
                    };
                    snapshot.Scripts[script.Id] = script;
                    script.Snapshot = snapshot;
                }
            }
        }

        private static void LoadScriptMachines(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacySnapshot snapshot)
        {
            if (!snapshot.Tables.Contains("ScriptMachines"))
            {
                return;
            }

            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT ScriptId, MachineId
FROM ScriptMachines
ORDER BY MachineId, ScriptId;"))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    snapshot.ScriptMachines.Add(new LegacyScriptMachineRow
                    {
                        ScriptId = reader.GetInt64(0),
                        MachineId = reader.GetInt32(1)
                    });
                }
            }
        }

        private static void LoadFieldMappings(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacySnapshot snapshot)
        {
            if (!snapshot.Tables.Contains("FieldMappings"))
            {
                return;
            }

            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT Id, ScriptId, ScriptVariable, ModelField
FROM FieldMappings
ORDER BY Id;"))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    snapshot.FieldMappings.Add(new LegacyFieldMappingRow
                    {
                        Id = reader.GetInt64(0),
                        ScriptId = reader.GetInt64(1),
                        ScriptVariable = reader.IsDBNull(2) ? null : reader.GetString(2),
                        ModelField = reader.IsDBNull(3) ? null : reader.GetString(3)
                    });
                }
            }
        }

        private static void LoadExistingTargetState(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacySnapshot snapshot)
        {
            if (snapshot.Tables.Contains("ParseRuleVersions"))
            {
                using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT Id, Status, ContentSha256
FROM ParseRuleVersions;"))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        snapshot.ExistingVersions[reader.GetInt64(0)] = new ExistingVersionRow
                        {
                            Id = reader.GetInt64(0),
                            Status = (ParseRuleStatus)reader.GetInt32(1),
                            ContentSha256 = reader.IsDBNull(2) ? null : reader.GetString(2)
                        };
                    }
                }
            }

            if (snapshot.Tables.Contains("PublishedParseRuleBindings"))
            {
                using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT MachineId, NormalizedExtension, ParseRuleVersionId
FROM PublishedParseRuleBindings;"))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string machineText = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                        int machineId;
                        if (!int.TryParse(machineText, NumberStyles.Integer, CultureInfo.InvariantCulture, out machineId))
                        {
                            continue;
                        }
                        string extension;
                        if (!TryNormalizeLegacyExtension(
                            reader.IsDBNull(1) ? null : reader.GetString(1),
                            out extension))
                        {
                            continue;
                        }
                        snapshot.ExistingBindings[new LegacyBindingKey(machineId, extension)] =
                            reader.GetInt64(2);
                    }
                }
            }

            if (!snapshot.Tables.Contains("LegacyParseRuleImports"))
            {
                return;
            }

            if (!snapshot.Tables.Contains("ParseRuleDefinitions") ||
                !snapshot.Tables.Contains("ParseRuleVersions"))
            {
                AddIssue(
                    snapshot.Report,
                    "INVALID_IMPORT_MAP_SCHEMA",
                    LegacyMigrationIssueSeverity.Blocking,
                    null,
                    null,
                    null,
                    "LegacyParseRuleImports exists without the parse-rule tables it references.");
                return;
            }

            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT ParseScriptId, DefinitionId, VersionId
FROM LegacyParseRuleImports
ORDER BY ParseScriptId;"))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var import = new ExistingImportRow
                    {
                        ParseScriptId = reader.GetInt64(0),
                        DefinitionId = reader.GetInt64(1),
                        VersionId = reader.GetInt64(2)
                    };
                    snapshot.ExistingImports[import.ParseScriptId] = import;
                }
            }

            foreach (ExistingImportRow import in snapshot.ExistingImports.Values)
            {
                if (!snapshot.ExistingVersions.ContainsKey(import.VersionId))
                {
                    AddIssue(
                        snapshot.Report,
                        "ORPHAN_IMPORT_MAP",
                        LegacyMigrationIssueSeverity.Blocking,
                        import.ParseScriptId,
                        null,
                        null,
                        "An existing legacy import points to a missing parse-rule version.");
                }
            }
        }

        private void AuditScripts(LegacySnapshot snapshot)
        {
            foreach (LegacyScriptRow script in snapshot.Scripts.Values.OrderBy(item => item.Id))
            {
                string normalizedExtension;
                if (!TryNormalizeLegacyExtension(script.RawExtension, out normalizedExtension))
                {
                    AddIssue(
                        snapshot.Report,
                        "INVALID_FILE_EXTENSION",
                        LegacyMigrationIssueSeverity.Blocking,
                        script.Id,
                        null,
                        null,
                        "The legacy script file extension is not a safe extension value.");
                }
                else
                {
                    script.NormalizedExtension = normalizedExtension;
                    if (!string.Equals(script.RawExtension, normalizedExtension, StringComparison.Ordinal))
                    {
                        AddIssue(
                            snapshot.Report,
                            "NON_CANONICAL_FILE_EXTENSION",
                            LegacyMigrationIssueSeverity.Warning,
                            script.Id,
                            null,
                            null,
                            "The legacy script extension will be normalized to " + normalizedExtension + ".");
                    }
                }

                if (string.IsNullOrWhiteSpace(script.Name) || string.IsNullOrWhiteSpace(script.ScriptCode))
                {
                    AddIssue(
                        snapshot.Report,
                        "INVALID_LEGACY_SCRIPT",
                        LegacyMigrationIssueSeverity.Blocking,
                        script.Id,
                        null,
                        null,
                        "The legacy script name and code must not be empty.");
                }

                LegacyModelRow model;
                if (!snapshot.Models.TryGetValue(script.ModelId, out model))
                {
                    AddIssue(
                        snapshot.Report,
                        "MISSING_MODEL",
                        LegacyMigrationIssueSeverity.Blocking,
                        script.Id,
                        null,
                        null,
                        "The legacy script references a missing data model.");
                    continue;
                }

                script.Model = model;
                bool hasValidBinding = snapshot.ScriptMachines.Any(
                    binding => binding.ScriptId == script.Id && MachineExists(snapshot, binding.MachineId));
                if (!GeneratedModelSourceExists(model.ModelName))
                {
                    AddIssue(
                        snapshot.Report,
                        "MISSING_GENERATED_MODEL_SOURCE",
                        script.IsEnabled && hasValidBinding
                            ? LegacyMigrationIssueSeverity.Blocking
                            : LegacyMigrationIssueSeverity.Warning,
                        script.Id,
                        null,
                        null,
                        "GeneratedModels source is missing for model " + model.ModelName + ".");
                }

                ExistingImportRow existingImport;
                ExistingVersionRow existingVersion;
                if (snapshot.ExistingImports.TryGetValue(script.Id, out existingImport) &&
                    snapshot.ExistingVersions.TryGetValue(existingImport.VersionId, out existingVersion) &&
                    !string.Equals(
                        existingVersion.ContentSha256,
                        ComputeSha256(script.ScriptCode ?? string.Empty),
                        StringComparison.Ordinal))
                {
                    AddIssue(
                        snapshot.Report,
                        "LEGACY_SCRIPT_CHANGED_AFTER_IMPORT",
                        LegacyMigrationIssueSeverity.Warning,
                        script.Id,
                        null,
                        null,
                        "The legacy script changed after it was imported; the immutable version was not overwritten.");
                }
            }
        }

        private static void AuditScriptMachines(LegacySnapshot snapshot)
        {
            foreach (LegacyScriptMachineRow binding in snapshot.ScriptMachines)
            {
                if (!snapshot.Scripts.ContainsKey(binding.ScriptId))
                {
                    AddIssue(
                        snapshot.Report,
                        "ORPHAN_SCRIPT_MACHINE_SCRIPT",
                        LegacyMigrationIssueSeverity.Blocking,
                        binding.ScriptId,
                        binding.MachineId,
                        null,
                        "ScriptMachines references a missing ParseScripts row.");
                }
                if (!MachineExists(snapshot, binding.MachineId))
                {
                    AddIssue(
                        snapshot.Report,
                        "ORPHAN_SCRIPT_MACHINE_MACHINE",
                        LegacyMigrationIssueSeverity.Blocking,
                        binding.ScriptId,
                        binding.MachineId,
                        null,
                        "ScriptMachines references a missing Machines row.");
                }
            }
        }

        private static void AuditFieldMappings(LegacySnapshot snapshot)
        {
            var fieldsByModel = snapshot.ModelFields
                .GroupBy(field => field.ModelId)
                .ToDictionary(
                    group => group.Key,
                    group => new HashSet<string>(
                        group.Select(field => field.FieldName),
                        StringComparer.Ordinal));

            foreach (LegacyFieldMappingRow mapping in snapshot.FieldMappings)
            {
                LegacyScriptRow script;
                HashSet<string> modelFields;
                bool isValid =
                    snapshot.Scripts.TryGetValue(mapping.ScriptId, out script) &&
                    !string.IsNullOrWhiteSpace(mapping.ScriptVariable) &&
                    !string.IsNullOrWhiteSpace(mapping.ModelField) &&
                    fieldsByModel.TryGetValue(script.ModelId, out modelFields) &&
                    modelFields.Contains(mapping.ModelField);

                if (!isValid)
                {
                    AddIssue(
                        snapshot.Report,
                        "INVALID_FIELD_MAPPING",
                        LegacyMigrationIssueSeverity.Warning,
                        mapping.ScriptId,
                        null,
                        mapping.Id,
                        "FieldMappings contains a missing script, blank name, or unknown model field.");
                }
            }
        }

        private static void BuildBindingConflicts(LegacySnapshot snapshot)
        {
            foreach (IGrouping<LegacyBindingKey, LegacyScriptRow> group in GetEligibleBindings(snapshot)
                .GroupBy(item => item.Key, item => item.Script)
                .Where(group => group.Select(script => script.Id).Distinct().Count() > 1)
                .OrderBy(group => group.Key.MachineId)
                .ThenBy(group => group.Key.NormalizedExtension, StringComparer.Ordinal))
            {
                var conflict = new LegacyBindingConflict
                {
                    MachineId = group.Key.MachineId,
                    NormalizedExtension = group.Key.NormalizedExtension
                };
                foreach (LegacyScriptRow script in group
                    .GroupBy(item => item.Id)
                    .Select(item => item.First())
                    .OrderBy(item => item.Id))
                {
                    conflict.Candidates.Add(new LegacyScriptCandidate
                    {
                        ParseScriptId = script.Id,
                        ScriptName = script.Name,
                        ModelId = script.ModelId,
                        NormalizedExtension = script.NormalizedExtension
                    });
                    conflict.CandidateScriptIds.Add(script.Id);
                }
                snapshot.Report.BindingConflicts.Add(conflict);
            }
        }

        private static IEnumerable<EligibleBinding> GetEligibleBindings(LegacySnapshot snapshot)
        {
            foreach (LegacyScriptMachineRow binding in snapshot.ScriptMachines)
            {
                LegacyScriptRow script;
                if (!snapshot.Scripts.TryGetValue(binding.ScriptId, out script) ||
                    !MachineExists(snapshot, binding.MachineId) ||
                    !script.IsEnabled ||
                    string.IsNullOrEmpty(script.NormalizedExtension) ||
                    script.Model == null)
                {
                    continue;
                }

                yield return new EligibleBinding
                {
                    Key = new LegacyBindingKey(binding.MachineId, script.NormalizedExtension),
                    Script = script
                };
            }
        }

        private static bool MachineExists(LegacySnapshot snapshot, int machineId)
        {
            if (machineId <= 0)
            {
                return false;
            }
            return !snapshot.Tables.Contains("Machines") || snapshot.MachineIds.Contains(machineId);
        }

        private static Dictionary<LegacyBindingKey, long> SelectOnlyUnambiguousBindings(
            LegacySnapshot snapshot)
        {
            return GetEligibleBindings(snapshot)
                .GroupBy(item => item.Key)
                .Where(group => group.Select(item => item.Script.Id).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => group.First().Script.Id);
        }

        private static Dictionary<LegacyBindingKey, long> ResolveSelectedBindings(
            LegacySnapshot snapshot,
            IList<LegacyBindingChoice> choices)
        {
            var selected = SelectOnlyUnambiguousBindings(snapshot);
            var choicesByKey = new Dictionary<LegacyBindingKey, LegacyBindingChoice>();

            foreach (LegacyBindingChoice choice in choices)
            {
                if (choice == null)
                {
                    throw new LegacyMigrationConflictException(
                        "A null legacy binding choice was supplied.",
                        CreateChoiceIssueReport("INVALID_BINDING_CHOICE", "Binding choice cannot be null."));
                }

                string extension;
                if (!TryNormalizeLegacyExtension(choice.NormalizedExtension, out extension))
                {
                    throw new LegacyMigrationConflictException(
                        "A legacy binding choice contains an invalid extension.",
                        CreateChoiceIssueReport("INVALID_BINDING_CHOICE", "Binding choice extension is invalid."));
                }

                var key = new LegacyBindingKey(choice.MachineId, extension);
                if (choicesByKey.ContainsKey(key))
                {
                    throw new LegacyMigrationConflictException(
                        "More than one choice was supplied for the same binding conflict.",
                        CreateChoiceIssueReport("DUPLICATE_BINDING_CHOICE", "Only one choice is allowed per conflict."));
                }
                choicesByKey.Add(key, choice);
            }

            foreach (LegacyBindingConflict conflict in snapshot.Report.BindingConflicts)
            {
                var key = new LegacyBindingKey(conflict.MachineId, conflict.NormalizedExtension);
                LegacyBindingChoice choice;
                if (!choicesByKey.TryGetValue(key, out choice))
                {
                    throw new LegacyMigrationConflictException(
                        "Legacy binding conflicts require an explicit user choice before migration.",
                        snapshot.Report);
                }
                if (!conflict.Candidates.Any(
                    candidate => candidate.ParseScriptId == choice.SelectedScriptId))
                {
                    throw new LegacyMigrationConflictException(
                        "The selected legacy script is not a candidate for its binding conflict.",
                        snapshot.Report);
                }

                selected[key] = choice.SelectedScriptId;
                choicesByKey.Remove(key);
            }

            if (choicesByKey.Count != 0)
            {
                throw new LegacyMigrationConflictException(
                    "A binding choice was supplied for a key that has no conflict.",
                    CreateChoiceIssueReport(
                        "UNEXPECTED_BINDING_CHOICE",
                        "Binding choices are accepted only for reported duplicate enabled bindings."));
            }
            return selected;
        }

        private static void AddPublishedBindingIssues(
            LegacySnapshot snapshot,
            IDictionary<LegacyBindingKey, long> selectedBindings)
        {
            foreach (KeyValuePair<LegacyBindingKey, long> selected in selectedBindings)
            {
                long existingVersionId;
                if (!snapshot.ExistingBindings.TryGetValue(selected.Key, out existingVersionId))
                {
                    continue;
                }

                if (snapshot.ExistingImports.ContainsKey(selected.Value))
                {
                    // The legacy row has already been placed under version control.
                    // Later editor saves or visual-rule publications are authoritative;
                    // startup migration must never reclaim their binding.
                    continue;
                }

                if (snapshot.Report.Issues.Any(issue =>
                    issue.Code == "PUBLISHED_BINDING_CONFLICT" &&
                    issue.ParseScriptId == selected.Value &&
                    issue.MachineId == selected.Key.MachineId))
                {
                    continue;
                }

                AddIssue(
                    snapshot.Report,
                    "PUBLISHED_BINDING_CONFLICT",
                    LegacyMigrationIssueSeverity.Blocking,
                    selected.Value,
                    selected.Key.MachineId,
                    null,
                    "The target machine and extension are already bound to another published version.");
            }
        }

        private static void ThrowIfBlocked(LegacyMigrationReport report)
        {
            if (!report.Issues.Any(
                issue => issue.Severity == LegacyMigrationIssueSeverity.Blocking))
            {
                return;
            }
            throw new LegacyMigrationConflictException(
                "Legacy parse-rule migration is blocked by integrity issues.",
                report);
        }

        private static LegacyMigrationReport CreateChoiceIssueReport(string code, string message)
        {
            var report = new LegacyMigrationReport();
            AddIssue(
                report,
                code,
                LegacyMigrationIssueSeverity.Blocking,
                null,
                null,
                null,
                message);
            return report;
        }

        private static void EnsureTargetSchema(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int databaseUserVersion)
        {
            if (databaseUserVersion > CompatibleSchemaVersion)
            {
                throw new ParseRuleStateException("The database schema is newer than this migrator supports.");
            }

            ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS ParseRuleDefinitions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RuleName TEXT NOT NULL,
    ModelId INTEGER NOT NULL,
    TargetModelType TEXT NOT NULL,
    NormalizedExtension TEXT NOT NULL,
    LegacyScriptId INTEGER NULL,
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

CREATE TABLE IF NOT EXISTS ParseRuleVersionModels (
    ParseRuleVersionId INTEGER NOT NULL,
    Role TEXT NOT NULL,
    ModelId INTEGER NOT NULL,
    ModelType TEXT NOT NULL,
    ModelSchemaHash TEXT NOT NULL,
    GeneratedModelCodeSnapshot TEXT NOT NULL,
    GeneratedModelCodeSha256 TEXT NOT NULL,
    PRIMARY KEY (ParseRuleVersionId, Role),
    CONSTRAINT FK_ParseRuleVersionModels_Version
        FOREIGN KEY (ParseRuleVersionId) REFERENCES ParseRuleVersions(Id) ON DELETE RESTRICT
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

CREATE TABLE IF NOT EXISTS LegacyParseRuleImports (
    ParseScriptId INTEGER NOT NULL PRIMARY KEY,
    DefinitionId INTEGER NOT NULL UNIQUE,
    VersionId INTEGER NOT NULL UNIQUE,
    ImportedTime DATETIME NOT NULL,
    CONSTRAINT FK_LegacyParseRuleImports_Definition
        FOREIGN KEY (DefinitionId) REFERENCES ParseRuleDefinitions(Id) ON DELETE RESTRICT,
    CONSTRAINT FK_LegacyParseRuleImports_Version
        FOREIGN KEY (VersionId) REFERENCES ParseRuleVersions(Id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS IX_ParseRuleVersions_DefinitionId
    ON ParseRuleVersions(DefinitionId, VersionNumber);
CREATE INDEX IF NOT EXISTS IX_ParseRuleVersionModels_ModelId
    ON ParseRuleVersionModels(ModelId, ParseRuleVersionId);
CREATE INDEX IF NOT EXISTS IX_PublishedParseRuleBindings_VersionId
    ON PublishedParseRuleBindings(ParseRuleVersionId);
");

            if (!ColumnExists(connection, transaction, "ParseRuleDefinitions", "LegacyScriptId"))
            {
                ExecuteNonQuery(
                    connection,
                    transaction,
                    "ALTER TABLE ParseRuleDefinitions ADD COLUMN LegacyScriptId INTEGER NULL;");
            }

            ExecuteNonQuery(connection, transaction, @"
CREATE INDEX IF NOT EXISTS IX_ParseRuleDefinitions_ModelId
    ON ParseRuleDefinitions(ModelId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_ParseRuleDefinitions_LegacyScriptId
    ON ParseRuleDefinitions(LegacyScriptId)
    WHERE LegacyScriptId IS NOT NULL;

UPDATE ParseRuleDefinitions
SET LegacyScriptId = (
    SELECT legacyImport.ParseScriptId
    FROM LegacyParseRuleImports legacyImport
    WHERE legacyImport.DefinitionId = ParseRuleDefinitions.Id)
WHERE LegacyScriptId IS NULL
  AND EXISTS (
      SELECT 1
      FROM LegacyParseRuleImports legacyImport
      WHERE legacyImport.DefinitionId = ParseRuleDefinitions.Id);
");
            ExecuteNonQuery(
                connection,
                transaction,
                "PRAGMA user_version = " + CompatibleSchemaVersion.ToString(CultureInfo.InvariantCulture) + ";");
        }

        private static void ImportLegacyScripts(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacySnapshot snapshot,
            IDictionary<LegacyBindingKey, long> selectedBindings)
        {
            var selectedScriptIds = new HashSet<long>(selectedBindings.Values);
            var imports = new Dictionary<long, ExistingImportRow>(snapshot.ExistingImports);
            var previouslyImportedScriptIds = new HashSet<long>(snapshot.ExistingImports.Keys);
            DateTime now = DateTime.UtcNow;

            foreach (LegacyScriptRow script in snapshot.Scripts.Values.OrderBy(item => item.Id))
            {
                ExistingImportRow existingImport;
                if (imports.TryGetValue(script.Id, out existingImport))
                {
                    continue;
                }

                bool isPublished = selectedScriptIds.Contains(script.Id);
                DateTime createdTime = script.CreateTime ?? now;
                DateTime updatedTime = script.UpdateTime ?? createdTime;
                long definitionId = InsertDefinition(
                    connection,
                    transaction,
                    script,
                    createdTime,
                    updatedTime);
                long versionId = InsertLegacyVersion(
                    connection,
                    transaction,
                    script,
                    definitionId,
                    isPublished,
                    createdTime,
                    isPublished ? (DateTime?)updatedTime : null);
                InsertImportMap(connection, transaction, script.Id, definitionId, versionId, now);

                imports[script.Id] = new ExistingImportRow
                {
                    ParseScriptId = script.Id,
                    DefinitionId = definitionId,
                    VersionId = versionId
                };
            }

            foreach (KeyValuePair<LegacyBindingKey, long> selected in selectedBindings
                .OrderBy(item => item.Key.MachineId)
                .ThenBy(item => item.Key.NormalizedExtension, StringComparer.Ordinal))
            {
                if (previouslyImportedScriptIds.Contains(selected.Value))
                {
                    // Subsequent versions and bindings are managed by the versioned
                    // editor/publisher. Migration is import-only after the first pass.
                    continue;
                }

                ExistingImportRow import = imports[selected.Value];
                ExistingVersionRow existingVersion;
                if (snapshot.ExistingVersions.TryGetValue(import.VersionId, out existingVersion))
                {
                    if (existingVersion.Status == ParseRuleStatus.Superseded)
                    {
                        throw new ParseRuleStateException(
                            "A superseded imported legacy version cannot be republished automatically.");
                    }

                    if (existingVersion.Status != ParseRuleStatus.Published)
                    {
                        using (SQLiteCommand publishCommand = CreateCommand(connection, transaction, @"
UPDATE ParseRuleVersions
SET Status = @PublishedStatus,
    Revision = Revision + 1,
    PublishedTime = COALESCE(PublishedTime, @PublishedTime)
WHERE Id = @VersionId AND Status IN (@DraftStatus, @ValidatedStatus);"))
                        {
                            AddParameter(publishCommand, "@PublishedStatus", DbType.Int32, (int)ParseRuleStatus.Published);
                            AddParameter(publishCommand, "@PublishedTime", DbType.DateTime, now);
                            AddParameter(publishCommand, "@VersionId", DbType.Int64, import.VersionId);
                            AddParameter(publishCommand, "@DraftStatus", DbType.Int32, (int)ParseRuleStatus.Draft);
                            AddParameter(publishCommand, "@ValidatedStatus", DbType.Int32, (int)ParseRuleStatus.Validated);
                            publishCommand.ExecuteNonQuery();
                        }
                    }
                }

                long existingBindingVersionId;
                if (snapshot.ExistingBindings.TryGetValue(selected.Key, out existingBindingVersionId))
                {
                    continue;
                }

                using (SQLiteCommand bindingCommand = CreateCommand(connection, transaction, @"
INSERT INTO PublishedParseRuleBindings
    (MachineId, NormalizedExtension, ParseRuleVersionId, PublishedTime)
VALUES
    (@MachineId, @NormalizedExtension, @VersionId, @PublishedTime);"))
                {
                    AddParameter(
                        bindingCommand,
                        "@MachineId",
                        DbType.String,
                        selected.Key.MachineId.ToString(CultureInfo.InvariantCulture));
                    AddParameter(
                        bindingCommand,
                        "@NormalizedExtension",
                        DbType.String,
                        selected.Key.NormalizedExtension);
                    AddParameter(bindingCommand, "@VersionId", DbType.Int64, import.VersionId);
                    AddParameter(bindingCommand, "@PublishedTime", DbType.DateTime, now);
                    bindingCommand.ExecuteNonQuery();
                }
            }
        }

        private static long InsertDefinition(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacyScriptRow script,
            DateTime createdTime,
            DateTime updatedTime)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
INSERT INTO ParseRuleDefinitions
    (RuleName, ModelId, TargetModelType, NormalizedExtension, LegacyScriptId,
     CreatedTime, UpdatedTime)
VALUES
    (@RuleName, @ModelId, @TargetModelType, @NormalizedExtension, @LegacyScriptId,
     @CreatedTime, @UpdatedTime);"))
            {
                AddParameter(command, "@RuleName", DbType.String, script.Name.Trim());
                AddParameter(command, "@ModelId", DbType.Int32, script.ModelId);
                AddParameter(command, "@TargetModelType", DbType.String, script.Model.ModelName);
                AddParameter(command, "@NormalizedExtension", DbType.String, script.NormalizedExtension);
                AddParameter(command, "@LegacyScriptId", DbType.Int64, script.Id);
                AddParameter(command, "@CreatedTime", DbType.DateTime, createdTime);
                AddParameter(command, "@UpdatedTime", DbType.DateTime, updatedTime);
                command.ExecuteNonQuery();
            }
            return Convert.ToInt64(
                ExecuteScalar(connection, transaction, "SELECT last_insert_rowid();"),
                CultureInfo.InvariantCulture);
        }

        private static long InsertLegacyVersion(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            LegacyScriptRow script,
            long definitionId,
            bool isPublished,
            DateTime createdTime,
            DateTime? publishedTime)
        {
            string definitionJson = JsonConvert.SerializeObject(
                new LegacyDefinitionDescriptor { LegacyParseScriptId = script.Id },
                Formatting.None);
            string validationSummary = JsonConvert.SerializeObject(
                new LegacyValidationDescriptor
                {
                    Source = "legacy-import",
                    Validated = false
                },
                Formatting.None);

            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
INSERT INTO ParseRuleVersions
    (DefinitionId, VersionNumber, Revision, RuleType, Status, DefinitionJson,
     DerivedScriptCode, ContentSha256, ModelSchemaHash, ValidationSummary,
     CreatedTime, ValidatedTime, PublishedTime)
VALUES
    (@DefinitionId, 1, 1, @RuleType, @Status, @DefinitionJson,
     @DerivedScriptCode, @ContentSha256, @ModelSchemaHash, @ValidationSummary,
     @CreatedTime, NULL, @PublishedTime);"))
            {
                AddParameter(command, "@DefinitionId", DbType.Int64, definitionId);
                AddParameter(command, "@RuleType", DbType.Int32, (int)ParseRuleType.LegacyCode);
                AddParameter(
                    command,
                    "@Status",
                    DbType.Int32,
                    (int)(isPublished ? ParseRuleStatus.Published : ParseRuleStatus.Draft));
                AddParameter(command, "@DefinitionJson", DbType.String, definitionJson);
                AddParameter(command, "@DerivedScriptCode", DbType.String, script.ScriptCode);
                AddParameter(command, "@ContentSha256", DbType.String, ComputeSha256(script.ScriptCode));
                AddParameter(command, "@ModelSchemaHash", DbType.String, ComputeModelSchemaHash(script));
                AddParameter(command, "@ValidationSummary", DbType.String, validationSummary);
                AddParameter(command, "@CreatedTime", DbType.DateTime, createdTime);
                AddParameter(command, "@PublishedTime", DbType.DateTime, publishedTime);
                command.ExecuteNonQuery();
            }
            return Convert.ToInt64(
                ExecuteScalar(connection, transaction, "SELECT last_insert_rowid();"),
                CultureInfo.InvariantCulture);
        }

        private static void InsertImportMap(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long parseScriptId,
            long definitionId,
            long versionId,
            DateTime importedTime)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
INSERT INTO LegacyParseRuleImports
    (ParseScriptId, DefinitionId, VersionId, ImportedTime)
VALUES
    (@ParseScriptId, @DefinitionId, @VersionId, @ImportedTime);"))
            {
                AddParameter(command, "@ParseScriptId", DbType.Int64, parseScriptId);
                AddParameter(command, "@DefinitionId", DbType.Int64, definitionId);
                AddParameter(command, "@VersionId", DbType.Int64, versionId);
                AddParameter(command, "@ImportedTime", DbType.DateTime, importedTime);
                command.ExecuteNonQuery();
            }
        }

        private static string ComputeModelSchemaHash(LegacyScriptRow script)
        {
            return ModelSchemaService.ComputeHash(script.Snapshot.ModelFields
                .Where(item => item.ModelId == script.ModelId)
                .OrderBy(item => item.FieldName, StringComparer.Ordinal)
                .ThenBy(item => item.Id)
                .Select(field => new ModelSchemaField
                {
                    FieldName = field.FieldName,
                    FieldType = field.FieldType,
                    FieldLength = field.FieldLength,
                    IsRequired = field.IsRequired,
                    IsPrimaryKey = field.IsPrimaryKey,
                    IsIdentity = field.IsIdentity
                }));
        }

        private bool GeneratedModelSourceExists(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName) ||
                !string.Equals(Path.GetFileName(modelName), modelName, StringComparison.Ordinal) ||
                modelName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            string candidate = Path.GetFullPath(Path.Combine(_generatedModelsDirectory, modelName + ".cs"));
            string prefix = _generatedModelsDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate);
        }

        private static bool TryNormalizeLegacyExtension(string extension, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            string candidate = extension.Trim().ToLowerInvariant();
            if (candidate.Length < 2 || candidate.Length > 17 || candidate[0] != '.')
            {
                return false;
            }
            for (int index = 1; index < candidate.Length; index++)
            {
                char value = candidate[index];
                if (!((value >= 'a' && value <= 'z') || (value >= '0' && value <= '9')))
                {
                    return false;
                }
            }
            normalized = candidate;
            return true;
        }

        private static string ComputeSha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string BuildConnectionString(
            string databasePath,
            bool readOnly,
            bool failIfMissing)
        {
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = databasePath,
                Version = 3,
                ForeignKeys = true,
                BusyTimeout = BusyTimeoutMilliseconds,
                Pooling = false,
                ReadOnly = readOnly,
                FailIfMissing = failIfMissing
            };
            return builder.ConnectionString;
        }

        private static void ConfigureConnection(SQLiteConnection connection)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = " +
                    BusyTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture) +
                    ";";
                command.ExecuteNonQuery();
            }
        }

        private static SQLiteCommand CreateCommand(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string commandText)
        {
            SQLiteCommand command = connection.CreateCommand();
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
            string name,
            DbType dbType,
            object value)
        {
            SQLiteParameter parameter = command.Parameters.Add(name, dbType);
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

        private static bool ColumnExists(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string tableName,
            string columnName)
        {
            using (SQLiteCommand command = CreateCommand(connection, transaction, @"
SELECT COUNT(*)
FROM pragma_table_info(@TableName)
WHERE name = @ColumnName;"))
            {
                AddParameter(command, "@TableName", DbType.String, tableName);
                AddParameter(command, "@ColumnName", DbType.String, columnName);
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
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

        private static void AddIssue(
            LegacyMigrationReport report,
            string code,
            LegacyMigrationIssueSeverity severity,
            long? parseScriptId,
            int? machineId,
            long? fieldMappingId,
            string message)
        {
            report.Issues.Add(new LegacyMigrationIssue
            {
                Code = code,
                Severity = severity,
                ParseScriptId = parseScriptId,
                MachineId = machineId,
                FieldMappingId = fieldMappingId,
                Message = message
            });
        }

        private sealed class LegacySnapshot
        {
            public LegacySnapshot()
            {
                Report = new LegacyMigrationReport();
                Tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Scripts = new Dictionary<long, LegacyScriptRow>();
                Models = new Dictionary<int, LegacyModelRow>();
                ModelFields = new List<LegacyModelFieldRow>();
                MachineIds = new HashSet<int>();
                ScriptMachines = new List<LegacyScriptMachineRow>();
                FieldMappings = new List<LegacyFieldMappingRow>();
                ExistingImports = new Dictionary<long, ExistingImportRow>();
                ExistingVersions = new Dictionary<long, ExistingVersionRow>();
                ExistingBindings = new Dictionary<LegacyBindingKey, long>();
            }

            public int DatabaseUserVersion { get; set; }
            public LegacyMigrationReport Report { get; private set; }
            public HashSet<string> Tables { get; set; }
            public Dictionary<long, LegacyScriptRow> Scripts { get; private set; }
            public Dictionary<int, LegacyModelRow> Models { get; private set; }
            public List<LegacyModelFieldRow> ModelFields { get; private set; }
            public HashSet<int> MachineIds { get; private set; }
            public List<LegacyScriptMachineRow> ScriptMachines { get; private set; }
            public List<LegacyFieldMappingRow> FieldMappings { get; private set; }
            public Dictionary<long, ExistingImportRow> ExistingImports { get; private set; }
            public Dictionary<long, ExistingVersionRow> ExistingVersions { get; private set; }
            public Dictionary<LegacyBindingKey, long> ExistingBindings { get; private set; }
        }

        private sealed class LegacyScriptRow
        {
            public long Id { get; set; }
            public string Name { get; set; }
            public int ModelId { get; set; }
            public string RawExtension { get; set; }
            public string NormalizedExtension { get; set; }
            public string ScriptCode { get; set; }
            public bool IsEnabled { get; set; }
            public DateTime? CreateTime { get; set; }
            public DateTime? UpdateTime { get; set; }
            public LegacyModelRow Model { get; set; }
            public LegacySnapshot Snapshot { get; set; }
        }

        private sealed class LegacyModelRow
        {
            public int Id { get; set; }
            public string ModelName { get; set; }
            public string TableName { get; set; }
            public int ParentModelId { get; set; }
        }

        private sealed class LegacyModelFieldRow
        {
            public long Id { get; set; }
            public int ModelId { get; set; }
            public string FieldName { get; set; }
            public string FieldType { get; set; }
            public int FieldLength { get; set; }
            public bool IsRequired { get; set; }
            public bool IsPrimaryKey { get; set; }
            public bool IsIdentity { get; set; }
        }

        private sealed class LegacyScriptMachineRow
        {
            public long ScriptId { get; set; }
            public int MachineId { get; set; }
        }

        private sealed class LegacyFieldMappingRow
        {
            public long Id { get; set; }
            public long ScriptId { get; set; }
            public string ScriptVariable { get; set; }
            public string ModelField { get; set; }
        }

        private sealed class ExistingImportRow
        {
            public long ParseScriptId { get; set; }
            public long DefinitionId { get; set; }
            public long VersionId { get; set; }
        }

        private sealed class ExistingVersionRow
        {
            public long Id { get; set; }
            public ParseRuleStatus Status { get; set; }
            public string ContentSha256 { get; set; }
        }

        private sealed class EligibleBinding
        {
            public LegacyBindingKey Key { get; set; }
            public LegacyScriptRow Script { get; set; }
        }

        private sealed class LegacyDefinitionDescriptor
        {
            [JsonProperty("legacyParseScriptId")]
            public long LegacyParseScriptId { get; set; }
        }

        private sealed class LegacyValidationDescriptor
        {
            [JsonProperty("source")]
            public string Source { get; set; }

            [JsonProperty("validated")]
            public bool Validated { get; set; }
        }

        private sealed class LegacyBindingKey : IEquatable<LegacyBindingKey>
        {
            public LegacyBindingKey(int machineId, string normalizedExtension)
            {
                MachineId = machineId;
                NormalizedExtension = normalizedExtension;
            }

            public int MachineId { get; private set; }
            public string NormalizedExtension { get; private set; }

            public bool Equals(LegacyBindingKey other)
            {
                return other != null &&
                       MachineId == other.MachineId &&
                       string.Equals(
                           NormalizedExtension,
                           other.NormalizedExtension,
                           StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as LegacyBindingKey);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (MachineId * 397) ^
                           (NormalizedExtension == null
                               ? 0
                               : StringComparer.Ordinal.GetHashCode(NormalizedExtension));
                }
            }
        }
    }
}
