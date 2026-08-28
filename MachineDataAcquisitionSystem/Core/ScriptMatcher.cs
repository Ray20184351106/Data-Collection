using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using MachineDataAcquisitionSystem.Core.Mapping;
using MachineDataAcquisitionSystem.Helpers;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Core
{
    public static class ScriptMatcher
    {
        public static ParseScript Match(int machineId, string filePath)
        {
            if (machineId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(machineId), "Machine id must be positive.");
            }

            string extension = NormalizeExtension(filePath);

            try
            {
                using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    connection.Open();

                    const string sql = @"
SELECT
    b.ParseRuleVersionId,
    v.DefinitionId,
    v.Status,
    v.RuleType,
    v.DefinitionJson,
    v.DerivedScriptCode,
    v.ContentSha256,
    v.ModelSchemaHash,
    d.RuleName,
    d.ModelId,
    d.TargetModelType,
    d.NormalizedExtension
FROM PublishedParseRuleBindings b
LEFT JOIN ParseRuleVersions v ON v.Id = b.ParseRuleVersionId
LEFT JOIN ParseRuleDefinitions d ON d.Id = v.DefinitionId
WHERE b.MachineId = @MachineId
  AND b.NormalizedExtension = @NormalizedExtension;";

                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.Add("@MachineId", DbType.String).Value =
                            machineId.ToString(CultureInfo.InvariantCulture);
                        command.Parameters.Add("@NormalizedExtension", DbType.String).Value = extension;

                        ParseScript script;
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                return null;
                            }

                            script = ReadPublishedScript(reader, extension);
                            if (reader.Read())
                            {
                                throw new InvalidOperationException(
                                    "More than one published parse-rule binding exists for the same machine and extension.");
                            }
                        }
                        LoadStoredModelSnapshots(connection, script);
                        return script.ModelSnapshots.Count > 0
                            ? script
                            : CaptureModelSourceSnapshot(script);
                    }
                }
            }
            catch (SQLiteException ex)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Failed to resolve the published parse rule for machine {0} and extension '{1}'.",
                        machineId,
                        extension),
                    ex);
            }
        }

        private static ParseScript ReadPublishedScript(SQLiteDataReader reader, string requestedExtension)
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) ||
                reader.IsDBNull(3) || reader.IsDBNull(4) || reader.IsDBNull(5) ||
                reader.IsDBNull(6) || reader.IsDBNull(7) || reader.IsDBNull(8) ||
                reader.IsDBNull(9) || reader.IsDBNull(10) || reader.IsDBNull(11))
            {
                throw new InvalidOperationException(
                    "The published parse-rule binding has missing version or definition data.");
            }

            long versionId = reader.GetInt64(0);
            long definitionId = reader.GetInt64(1);
            int status = reader.GetInt32(2);
            int ruleTypeValue = reader.GetInt32(3);
            string definitionJson = reader.GetString(4);
            string scriptCode = reader.GetString(5);
            string contentSha256 = reader.GetString(6);
            string modelSchemaHash = reader.GetString(7);
            string ruleName = reader.GetString(8);
            int modelId = reader.GetInt32(9);
            string targetModelType = reader.GetString(10);
            string normalizedExtension = reader.GetString(11);

            if (versionId <= 0)
            {
                throw new InvalidOperationException("The published parse-rule version id is invalid.");
            }
            if (status != (int)ParseRuleStatus.Published)
            {
                throw new InvalidOperationException(
                    "The published parse-rule binding points to a version that is not published.");
            }
            if (!Enum.IsDefined(typeof(ParseRuleType), ruleTypeValue))
            {
                throw new InvalidOperationException("The published parse-rule type is invalid.");
            }
            if (modelId <= 0 || string.IsNullOrWhiteSpace(ruleName) ||
                string.IsNullOrWhiteSpace(targetModelType))
            {
                throw new InvalidOperationException("The published parse-rule definition is incomplete.");
            }
            if (string.IsNullOrWhiteSpace(scriptCode) || string.IsNullOrWhiteSpace(contentSha256) ||
                string.IsNullOrWhiteSpace(modelSchemaHash))
            {
                throw new InvalidOperationException("The published parse-rule version metadata is incomplete.");
            }
            if (!string.Equals(normalizedExtension, requestedExtension, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The published parse-rule definition extension does not match its binding.");
            }

            var version = new ParseRuleVersion
            {
                Id = versionId,
                DefinitionId = definitionId,
                Status = (ParseRuleStatus)status,
                RuleType = (ParseRuleType)ruleTypeValue,
                DefinitionJson = definitionJson,
                DerivedScriptCode = scriptCode,
                ContentSha256 = contentSha256,
                ModelSchemaHash = modelSchemaHash,
                RuleName = ruleName,
                ModelId = modelId,
                TargetModelType = targetModelType,
                NormalizedExtension = normalizedExtension
            };
            ParseRuleIntegrityValidator.Validate(version);

            var result = new ParseScript
            {
                Id = versionId <= int.MaxValue ? (int)versionId : 0,
                Name = ruleName,
                ModelId = modelId,
                FileExtension = normalizedExtension,
                ScriptCode = scriptCode,
                IsEnabled = true,
                ParserVersionId = versionId,
                ContentSha256 = contentSha256,
                ModelSchemaHash = modelSchemaHash,
                RuleType = (ParseRuleType)ruleTypeValue,
                TargetModelType = targetModelType,
                DefinitionJson = definitionJson
            };
            return result;
        }

        private static void LoadStoredModelSnapshots(SQLiteConnection connection, ParseScript script)
        {
            using (SQLiteCommand tableCommand = connection.CreateCommand())
            {
                tableCommand.CommandText = @"
SELECT COUNT(1) FROM sqlite_master
WHERE type='table' AND name='ParseRuleVersionModels';";
                if (Convert.ToInt32(tableCommand.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                    return;
            }

            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT ModelId,ModelType,ModelSchemaHash,GeneratedModelCodeSnapshot,GeneratedModelCodeSha256
FROM ParseRuleVersionModels
WHERE ParseRuleVersionId=@VersionId
ORDER BY CASE Role WHEN 'Master' THEN 0 WHEN 'Detail' THEN 1 ELSE 2 END, Role;";
                command.Parameters.Add("@VersionId", DbType.Int64).Value = script.ParserVersionId.Value;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        script.ModelSnapshots.Add(new ParseScriptModelSnapshot
                        {
                            ModelId = reader.GetInt32(0),
                            ModelType = reader.GetString(1),
                            ModelSchemaHash = reader.GetString(2),
                            GeneratedModelCodeSnapshot = reader.GetString(3),
                            GeneratedModelCodeSha256 = reader.GetString(4)
                        });
                    }
                }
            }
            if (script.ModelSnapshots.Count == 0) return;
            ParseScriptModelSnapshot primary = script.ModelSnapshots[0];
            script.GeneratedModelCodeSnapshot = primary.GeneratedModelCodeSnapshot;
            script.GeneratedModelCodeSha256 = primary.GeneratedModelCodeSha256;
        }

        public static ParseScript CaptureModelSourceSnapshot(ParseScript script)
        {
            if (script == null)
            {
                throw new ArgumentNullException(nameof(script));
            }
            if (script.ParserVersionId.HasValue && script.ParserVersionId.Value > 0)
            {
                var targets = new System.Collections.Generic.List<MappingTargetDefinition>
                {
                    new MappingTargetDefinition
                    {
                        ModelId = script.ModelId,
                        TargetModelType = script.TargetModelType,
                        ModelSchemaHash = script.ModelSchemaHash
                    }
                };
                if (script.RuleType == ParseRuleType.Mapping &&
                    !string.IsNullOrWhiteSpace(script.DefinitionJson))
                {
                    MappingRuleDefinition definition = MappingRuleSerializer.Deserialize(script.DefinitionJson);
                    if (definition.RecordMode == MappingRecordMode.MasterDetail)
                    {
                        targets.Clear();
                        targets.Add(definition.MasterDetail.Master);
                        targets.Add(definition.MasterDetail.Detail);
                    }
                }

                script.ModelSnapshots = new System.Collections.Generic.List<ParseScriptModelSnapshot>();
                foreach (MappingTargetDefinition target in targets)
                {
                    string source = ScriptEngine.CaptureConfiguredModelCode(
                        target.ModelId,
                        target.TargetModelType);
                    script.ModelSnapshots.Add(new ParseScriptModelSnapshot
                    {
                        ModelId = target.ModelId,
                        ModelType = target.TargetModelType,
                        ModelSchemaHash = target.ModelSchemaHash,
                        GeneratedModelCodeSnapshot = source,
                        GeneratedModelCodeSha256 = MappingRuleSerializer.Sha256(source)
                    });
                }
                ParseScriptModelSnapshot primary = script.ModelSnapshots[0];
                script.GeneratedModelCodeSnapshot = primary.GeneratedModelCodeSnapshot;
                script.GeneratedModelCodeSha256 = primary.GeneratedModelCodeSha256;
            }
            return script;
        }

        private static string NormalizeExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            string extension = Path.GetExtension(filePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                throw new ArgumentException("File path must include an extension.", nameof(filePath));
            }

            return extension.Trim().ToLowerInvariant();
        }
    }
}
