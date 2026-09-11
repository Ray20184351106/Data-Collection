using System;
using System.Data.SQLite;
using System.IO;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class ConfigurationDeletionServiceTests
    {
        [Fact]
        public void DeleteMappingDefinition_removes_model_snapshots_but_preserves_other_rules()
        {
            using (var fixture = DeletionFixture.Create())
            using (var store = new ParseRuleStore(fixture.DatabasePath))
            {
                store.Initialize();
                var draft = store.SaveDraft(fixture.NewMappingRule());
                var validated = store.Validate(draft.Id, draft.Revision, "passed");
                store.Publish(validated.Id, "1", validated.Revision);
                var other = store.SaveDraft(fixture.NewMappingRule());
                foreach (long id in new[] { draft.Id, other.Id })
                    fixture.Execute("INSERT INTO ParseRuleVersionModels VALUES (" + id + ", 'Master', 7, 'InspectionRecord', 'hash', 'source', 'hash');");

                new ConfigurationDeletionService(fixture.DatabasePath).DeleteMappingDefinition(draft.DefinitionId);

                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM ParseRuleVersions WHERE DefinitionId=" + draft.DefinitionId));
                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM PublishedParseRuleBindings;"));
                Assert.Equal(1L, fixture.ScalarLong("SELECT COUNT(*) FROM ParseRuleVersionModels;"));
                Assert.Equal(1L, fixture.ScalarLong("SELECT COUNT(*) FROM ParseRuleVersions WHERE Id=" + other.Id));
            }
        }

        [Fact]
        public void DeleteMappingDefinition_removes_an_unpublished_definition_and_all_its_versions()
        {
            using (var fixture = DeletionFixture.Create())
            using (var store = new ParseRuleStore(fixture.DatabasePath))
            {
                store.Initialize();
                ParseRuleVersion draft = store.SaveDraft(fixture.NewMappingRule());

                new ConfigurationDeletionService(fixture.DatabasePath).DeleteMappingDefinition(draft.DefinitionId);

                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM ParseRuleDefinitions;"));
                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM ParseRuleVersions;"));
            }
        }

        [Fact]
        public void DeleteMappingDefinition_removes_a_published_rule_and_its_machine_bindings()
        {
            using (var fixture = DeletionFixture.Create())
            using (var store = new ParseRuleStore(fixture.DatabasePath))
            {
                store.Initialize();
                ParseRuleVersion draft = store.SaveDraft(fixture.NewMappingRule());
                ParseRuleVersion validated = store.Validate(draft.Id, draft.Revision, "passed");
                ParseRuleVersion published = store.Publish(validated.Id, "1", validated.Revision);

                new ConfigurationDeletionService(fixture.DatabasePath).DeleteMappingDefinition(published.DefinitionId);

                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM PublishedParseRuleBindings;"));
                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM ParseRuleVersions;"));
                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM ParseRuleDefinitions;"));
            }
        }

        [Fact]
        public void DeleteModel_rejects_a_model_referenced_by_a_parse_script()
        {
            using (var fixture = DeletionFixture.Create())
            {
                fixture.Execute("INSERT INTO ParseScripts (Id,Name,ModelId,FileExtension,ScriptCode,IsEnabled) VALUES (1,'script',7,'.xlsx','return null;',0);");

                Assert.Throws<ConfigurationDeletionBlockedException>(() =>
                    new ConfigurationDeletionService(fixture.DatabasePath).DeleteModel(7));
            }
        }

        [Fact]
        public void DeleteLegacyScript_removes_an_unpublished_script_and_its_legacy_relations()
        {
            using (var fixture = DeletionFixture.Create())
            {
                fixture.Execute(@"
INSERT INTO ParseScripts (Id,Name,ModelId,FileExtension,ScriptCode,IsEnabled) VALUES (1,'script',7,'.xlsx','return null;',0);
INSERT INTO ScriptMachines (ScriptId,MachineId) VALUES (1,1);
INSERT INTO FieldMappings (Id,ScriptId,ScriptVariable,ModelField) VALUES (1,1,'value','SerialNumber');");

                new ConfigurationDeletionService(fixture.DatabasePath).DeleteLegacyScript(1);

                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM ParseScripts;"));
                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM ScriptMachines;"));
                Assert.Equal(0L, fixture.ScalarLong("SELECT COUNT(*) FROM FieldMappings;"));
            }
        }

        private sealed class DeletionFixture : IDisposable
        {
            public string DatabasePath { get; private set; }

            public static DeletionFixture Create()
            {
                var fixture = new DeletionFixture
                {
                    DatabasePath = Path.Combine(Path.GetTempPath(), "Deletion_" + Guid.NewGuid().ToString("N") + ".db")
                };
                fixture.Execute(@"
CREATE TABLE DataModels (Id INTEGER PRIMARY KEY, ModelName TEXT NOT NULL, TableName TEXT NOT NULL, ParentModelId INTEGER DEFAULT 0);
CREATE TABLE ModelFields (Id INTEGER PRIMARY KEY, ModelId INTEGER NOT NULL, FieldName TEXT NOT NULL, FieldType TEXT NOT NULL, FieldLength INTEGER DEFAULT 0, IsRequired INTEGER DEFAULT 0, IsPrimaryKey INTEGER DEFAULT 0, IsIdentity INTEGER DEFAULT 0, Description TEXT);
CREATE TABLE ParseScripts (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, ModelId INTEGER NOT NULL, FileExtension TEXT NOT NULL, ScriptCode TEXT NOT NULL, IsEnabled INTEGER NOT NULL);
CREATE TABLE ScriptMachines (ScriptId INTEGER NOT NULL, MachineId INTEGER NOT NULL);
CREATE TABLE FieldMappings (Id INTEGER PRIMARY KEY, ScriptId INTEGER NOT NULL, ScriptVariable TEXT NOT NULL, ModelField TEXT NOT NULL);
INSERT INTO DataModels (Id,ModelName,TableName) VALUES (7,'InspectionRecord','InspectionRecord');
INSERT INTO ModelFields (Id,ModelId,FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,Description) VALUES (1,7,'SerialNumber','string',64,1,0,0,'serial');");
                return fixture;
            }

            public MappingRuleDefinition NewMappingRule()
            {
                return new MappingRuleDefinition
                {
                    RuleName = "mapping",
                    ModelId = 7,
                    TargetModelType = "InspectionRecord",
                    ModelSchemaHash = ModelSchemaService.ComputeHash(new[]
                    {
                        new ModelSchemaField { FieldName = "SerialNumber", FieldType = "string", FieldLength = 64, IsRequired = true, Description = "serial" }
                    }),
                    NormalizedExtension = ".xlsx",
                    SheetName = "Data",
                    Fields = { new FieldMappingRule { TargetField = "SerialNumber", TargetType = "string", IsRequired = true, Locator = new MappingLocator { Type = "cell", Cell = "A1", AnchorCell = "A1", AnchorText = "Serial" } } }
                };
            }

            public void Execute(string sql)
            {
                using (var connection = new SQLiteConnection("Data Source=" + DatabasePath + ";Version=3;"))
                using (var command = new SQLiteCommand(sql, connection))
                {
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }

            public long ScalarLong(string sql)
            {
                using (var connection = new SQLiteConnection("Data Source=" + DatabasePath + ";Version=3;"))
                using (var command = new SQLiteCommand(sql, connection))
                {
                    connection.Open();
                    return Convert.ToInt64(command.ExecuteScalar());
                }
            }

            public void Dispose()
            {
                if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
            }
        }
    }
}
