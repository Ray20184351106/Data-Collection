using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class ParseRuleDatabaseMigratorTests
    {
        [Fact]
        public void Duplicate_enabled_legacy_bindings_stop_before_schema_version_is_advanced()
        {
            using (LegacyFixture fixture = LegacyFixture.Create(duplicateBinding: true))
            {
                var migrator = new ParseRuleDatabaseMigrator(fixture.DatabasePath, fixture.ModelsDirectory);

                LegacyMigrationReport report = migrator.InspectLegacy();

                Assert.True(report.HasBlockingIssues);
                Assert.Single(report.Conflicts);
                Assert.Throws<LegacyMigrationConflictException>(() =>
                    migrator.Migrate(new List<LegacyBindingChoice>()));
                Assert.Equal(0, fixture.ReadUserVersion());
            }
        }

        [Fact]
        public void Unambiguous_legacy_script_is_imported_as_the_unique_published_version()
        {
            using (LegacyFixture fixture = LegacyFixture.Create(duplicateBinding: false))
            {
                var migrator = new ParseRuleDatabaseMigrator(fixture.DatabasePath, fixture.ModelsDirectory);

                migrator.Migrate(new List<LegacyBindingChoice>());

                using (var store = new ParseRuleStore(fixture.DatabasePath))
                {
                    store.Initialize();
                    ParseRuleVersion published = store.GetPublished("1", ".XLSX");
                    Assert.NotNull(published);
                    Assert.Equal(ParseRuleType.LegacyCode, published.RuleType);
                    Assert.Contains("return new InspectionRecord", published.DerivedScriptCode);
                    Assert.Equal(
                        new ModelSchemaService("Data Source=" + fixture.DatabasePath + ";Version=3;").ComputeHash(7),
                        published.ModelSchemaHash);
                }
                Assert.Equal(1, fixture.ReadUserVersion());
            }
        }

        [Fact]
        public void Explicit_choice_resolves_a_duplicate_without_silently_selecting_limit_one()
        {
            using (LegacyFixture fixture = LegacyFixture.Create(duplicateBinding: true))
            {
                var migrator = new ParseRuleDatabaseMigrator(fixture.DatabasePath, fixture.ModelsDirectory);

                migrator.Migrate(new List<LegacyBindingChoice>
                {
                    new LegacyBindingChoice
                    {
                        MachineId = 1,
                        NormalizedExtension = ".xlsx",
                        SelectedScriptId = 2
                    }
                });

                using (var store = new ParseRuleStore(fixture.DatabasePath))
                {
                    store.Initialize();
                    ParseRuleVersion published = store.GetPublished("1", ".xlsx");
                    Assert.NotNull(published);
                    Assert.Contains("ChosenScript2", published.DerivedScriptCode);
                }
            }
        }

        [Fact]
        public void Repeated_migration_preserves_a_visual_rule_that_replaced_an_imported_legacy_binding()
        {
            using (LegacyFixture fixture = LegacyFixture.Create(duplicateBinding: false))
            {
                var migrator = new ParseRuleDatabaseMigrator(fixture.DatabasePath, fixture.ModelsDirectory);
                migrator.Migrate(new List<LegacyBindingChoice>());

                long visualVersionId;
                using (var store = new ParseRuleStore(fixture.DatabasePath))
                {
                    store.Initialize();
                    var schema = new ModelSchemaService("Data Source=" + fixture.DatabasePath + ";Version=3;");
                    ParseRuleVersion draft = store.SaveDraft(new MappingRuleDefinition
                    {
                        RuleName = "visual",
                        ModelId = 7,
                        TargetModelType = "InspectionRecord",
                        ModelSchemaHash = schema.ComputeHash(7),
                        NormalizedExtension = ".xlsx",
                        SheetName = "Data",
                        Fields = new List<FieldMappingRule>
                        {
                            new FieldMappingRule
                            {
                                TargetField = "SerialNumber",
                                TargetType = "string",
                                IsRequired = true,
                                Locator = new MappingLocator
                                {
                                    Type = "cell",
                                    Cell = "A1",
                                    AnchorCell = "A1",
                                    AnchorText = "Template anchor"
                                }
                            }
                        }
                    });
                    ParseRuleVersion validated = store.Validate(draft.Id, draft.Revision, "preview passed");
                    ParseRuleVersion published = store.Publish(
                        validated.Id,
                        "1",
                        validated.Revision,
                        replaceExisting: true);
                    visualVersionId = published.Id;
                }

                migrator.Migrate(new List<LegacyBindingChoice>());

                using (var store = new ParseRuleStore(fixture.DatabasePath))
                {
                    store.Initialize();
                    Assert.Equal(visualVersionId, store.GetPublished("1", ".xlsx").Id);
                }
            }
        }

        [Fact]
        public void Repeated_migration_preserves_a_new_legacy_editor_version()
        {
            using (LegacyFixture fixture = LegacyFixture.Create(duplicateBinding: false))
            {
                var migrator = new ParseRuleDatabaseMigrator(fixture.DatabasePath, fixture.ModelsDirectory);
                migrator.Migrate(new List<LegacyBindingChoice>());

                var service = new LegacyScriptVersionService(fixture.DatabasePath);
                LegacyScriptSaveResult saved = service.Save(new LegacyScriptSaveRequest
                {
                    LegacyScriptId = 1,
                    Name = "legacy-1-edited",
                    ModelId = 7,
                    TargetModelType = "InspectionRecord",
                    FileExtension = ".xlsx",
                    ScriptCode = "return new InspectionRecord { SerialNumber = \"edited\" };",
                    IsEnabled = true,
                    MachineIds = new[] { 1 }
                });

                migrator.Migrate(new List<LegacyBindingChoice>());

                using (var store = new ParseRuleStore(fixture.DatabasePath))
                {
                    store.Initialize();
                    ParseRuleVersion published = store.GetPublished("1", ".xlsx");
                    Assert.Equal(saved.ParseRuleVersionId, published.Id);
                    Assert.Contains("edited", published.DerivedScriptCode);
                }
            }
        }

        private sealed class LegacyFixture : IDisposable
        {
            public string DatabasePath { get; private set; }
            public string ModelsDirectory { get; private set; }

            public static LegacyFixture Create(bool duplicateBinding)
            {
                string suffix = Guid.NewGuid().ToString("N");
                var fixture = new LegacyFixture
                {
                    DatabasePath = Path.Combine(Path.GetTempPath(), "LegacyMigration_" + suffix + ".db"),
                    ModelsDirectory = Path.Combine(Path.GetTempPath(), "LegacyModels_" + suffix)
                };
                Directory.CreateDirectory(fixture.ModelsDirectory);
                File.WriteAllText(
                    Path.Combine(fixture.ModelsDirectory, "InspectionRecord.cs"),
                    "public class InspectionRecord { public string SerialNumber { get; set; } }");

                using (var connection = new SQLiteConnection("Data Source=" + fixture.DatabasePath + ";Version=3;"))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = @"
CREATE TABLE DataModels (Id INTEGER PRIMARY KEY, ModelName TEXT NOT NULL, TableName TEXT NOT NULL);
CREATE TABLE ModelFields (Id INTEGER PRIMARY KEY, ModelId INTEGER, FieldName TEXT, FieldType TEXT, FieldLength INTEGER, IsRequired INTEGER, IsPrimaryKey INTEGER, IsIdentity INTEGER, Description TEXT);
CREATE TABLE ParseScripts (Id INTEGER PRIMARY KEY, Name TEXT, ModelId INTEGER, FileExtension TEXT, ScriptCode TEXT, IsEnabled INTEGER, UpdateTime DATETIME);
CREATE TABLE ScriptMachines (ScriptId INTEGER, MachineId INTEGER, PRIMARY KEY (ScriptId, MachineId));
CREATE TABLE FieldMappings (Id INTEGER PRIMARY KEY, ScriptId INTEGER, ScriptVariable TEXT, ModelField TEXT, TransformExpression TEXT);
CREATE TABLE FileProcessRecord (Id INTEGER PRIMARY KEY, MachineId INTEGER, FileName TEXT);
INSERT INTO DataModels (Id, ModelName, TableName) VALUES (7, 'InspectionRecord', 'InspectionRecord');
INSERT INTO ModelFields (Id, ModelId, FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description)
VALUES (1, 7, 'SerialNumber', 'string', 64, 1, 1, 0, 'serial');
INSERT INTO ParseScripts (Id, Name, ModelId, FileExtension, ScriptCode, IsEnabled)
VALUES (1, 'legacy-1', 7, '.xlsx', 'return new InspectionRecord();', 1);
INSERT INTO ScriptMachines (ScriptId, MachineId) VALUES (1, 1);";
                    command.ExecuteNonQuery();
                    if (duplicateBinding)
                    {
                        command.CommandText = @"
INSERT INTO ParseScripts (Id, Name, ModelId, FileExtension, ScriptCode, IsEnabled)
VALUES (2, 'legacy-2', 7, '.XLSX', 'return new InspectionRecord { SerialNumber = ""ChosenScript2"" };', 1);
INSERT INTO ScriptMachines (ScriptId, MachineId) VALUES (2, 1);";
                        command.ExecuteNonQuery();
                    }
                }
                return fixture;
            }

            public int ReadUserVersion()
            {
                using (var connection = new SQLiteConnection("Data Source=" + DatabasePath + ";Version=3;"))
                using (var command = new SQLiteCommand("PRAGMA user_version;", connection))
                {
                    connection.Open();
                    return Convert.ToInt32(command.ExecuteScalar());
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
