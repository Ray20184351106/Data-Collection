using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class LegacyScriptVersionServiceTests
    {
        [Fact]
        public void Save_updates_legacy_tables_and_published_version_in_one_transaction()
        {
            using (var fixture = LegacySaveFixture.Create(includeSecondScript: false))
            {
                ParseRuleVersion before = fixture.GetPublished(1, ".xlsx");
                var service = new LegacyScriptVersionService(fixture.DatabasePath);

                LegacyScriptSaveResult saved = service.Save(new LegacyScriptSaveRequest
                {
                    LegacyScriptId = 1,
                    Name = "legacy-updated",
                    ModelId = 7,
                    TargetModelType = "InspectionRecord",
                    FileExtension = ".xlsx",
                    ScriptCode = "return new InspectionRecord { SerialNumber = \"UpdatedVersion\" };",
                    IsEnabled = true,
                    MachineIds = new List<int> { 1 }
                });

                ParseRuleVersion after = fixture.GetPublished(1, ".xlsx");
                Assert.True(saved.ParseRuleVersionId > before.Id);
                Assert.Equal(saved.ParseRuleVersionId, after.Id);
                Assert.Contains("UpdatedVersion", after.DerivedScriptCode);
                Assert.Contains("UpdatedVersion", fixture.GetLegacyCode(1));
            }
        }

        [Fact]
        public void Save_binding_conflict_rolls_back_legacy_script_update()
        {
            using (var fixture = LegacySaveFixture.Create(includeSecondScript: true))
            {
                string original = fixture.GetLegacyCode(1);
                var service = new LegacyScriptVersionService(fixture.DatabasePath);

                Assert.Throws<ParseRuleBindingConflictException>(() => service.Save(new LegacyScriptSaveRequest
                {
                    LegacyScriptId = 1,
                    Name = "must-rollback",
                    ModelId = 7,
                    TargetModelType = "InspectionRecord",
                    FileExtension = ".xlsx",
                    ScriptCode = "return new InspectionRecord { SerialNumber = \"ShouldNotPersist\" };",
                    IsEnabled = true,
                    MachineIds = new List<int> { 2 }
                }));

                Assert.Equal(original, fixture.GetLegacyCode(1));
                Assert.DoesNotContain("ShouldNotPersist", fixture.GetPublished(1, ".xlsx").DerivedScriptCode);
            }
        }

        private sealed class LegacySaveFixture : IDisposable
        {
            public string DatabasePath { get; private set; }
            public string ModelsDirectory { get; private set; }

            public static LegacySaveFixture Create(bool includeSecondScript)
            {
                string suffix = Guid.NewGuid().ToString("N");
                var fixture = new LegacySaveFixture
                {
                    DatabasePath = Path.Combine(Path.GetTempPath(), "LegacySave_" + suffix + ".db"),
                    ModelsDirectory = Path.Combine(Path.GetTempPath(), "LegacySaveModels_" + suffix)
                };
                Directory.CreateDirectory(fixture.ModelsDirectory);
                File.WriteAllText(Path.Combine(fixture.ModelsDirectory, "InspectionRecord.cs"), "public class InspectionRecord { public string SerialNumber { get; set; } }");
                using (var connection = new SQLiteConnection("Data Source=" + fixture.DatabasePath + ";Version=3;"))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = @"
CREATE TABLE DataModels (Id INTEGER PRIMARY KEY, ModelName TEXT, TableName TEXT);
CREATE TABLE ModelFields (Id INTEGER PRIMARY KEY, ModelId INTEGER, FieldName TEXT, FieldType TEXT, FieldLength INTEGER, IsRequired INTEGER, IsPrimaryKey INTEGER, IsIdentity INTEGER, Description TEXT);
CREATE TABLE ParseScripts (Id INTEGER PRIMARY KEY, Name TEXT, ModelId INTEGER, FileExtension TEXT, ScriptCode TEXT, IsEnabled INTEGER, UpdateTime DATETIME);
CREATE TABLE ScriptMachines (ScriptId INTEGER, MachineId INTEGER, PRIMARY KEY (ScriptId, MachineId));
CREATE TABLE FieldMappings (Id INTEGER PRIMARY KEY, ScriptId INTEGER, ScriptVariable TEXT, ModelField TEXT, TransformExpression TEXT);
CREATE TABLE FileProcessRecord (Id INTEGER PRIMARY KEY, MachineId INTEGER, FileName TEXT);
INSERT INTO DataModels VALUES (7, 'InspectionRecord', 'InspectionRecord');
INSERT INTO ModelFields VALUES (1, 7, 'SerialNumber', 'string', 100, 1, 0, 0, 'serial');
INSERT INTO ParseScripts VALUES (1, 'legacy-1', 7, '.xlsx', 'return new InspectionRecord();', 1, CURRENT_TIMESTAMP);
INSERT INTO ScriptMachines VALUES (1, 1);";
                    command.ExecuteNonQuery();
                    if (includeSecondScript)
                    {
                        command.CommandText = @"
INSERT INTO ParseScripts VALUES (2, 'legacy-2', 7, '.xlsx', 'return new InspectionRecord();', 1, CURRENT_TIMESTAMP);
INSERT INTO ScriptMachines VALUES (2, 2);";
                        command.ExecuteNonQuery();
                    }
                }
                new ParseRuleDatabaseMigrator(fixture.DatabasePath, fixture.ModelsDirectory)
                    .Migrate(new List<LegacyBindingChoice>());
                return fixture;
            }

            public ParseRuleVersion GetPublished(int machineId, string extension)
            {
                using (var store = new ParseRuleStore(DatabasePath))
                {
                    store.Initialize();
                    return store.GetPublished(machineId.ToString(), extension);
                }
            }

            public string GetLegacyCode(long scriptId)
            {
                using (var connection = new SQLiteConnection("Data Source=" + DatabasePath + ";Version=3;"))
                using (var command = new SQLiteCommand("SELECT ScriptCode FROM ParseScripts WHERE Id=@Id", connection))
                {
                    connection.Open();
                    command.Parameters.AddWithValue("@Id", scriptId);
                    return Convert.ToString(command.ExecuteScalar());
                }
            }

            public void Dispose()
            {
                string modelFile = Path.Combine(ModelsDirectory, "InspectionRecord.cs");
                if (File.Exists(modelFile)) File.Delete(modelFile);
                if (Directory.Exists(ModelsDirectory)) Directory.Delete(ModelsDirectory);
                if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
            }
        }
    }
}
