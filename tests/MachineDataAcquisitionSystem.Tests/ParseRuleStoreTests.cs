using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class ParseRuleStoreTests
    {
        [Fact]
        public void SaveDraft_persists_to_a_real_sqlite_file_but_does_not_publish()
        {
            string databasePath = NewDatabasePath();
            try
            {
                using (var store = OpenStore(databasePath))
                {
                    ParseRuleVersion draft = store.SaveDraft(NewRule("rule-a", "A1"));

                    Assert.Equal(ParseRuleStatus.Draft, draft.Status);
                    Assert.Null(store.GetPublished("Machine1", ".xlsx"));
                    Assert.True(File.Exists(databasePath));
                    Assert.True(new FileInfo(databasePath).Length > 0);
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        [Fact]
        public void Publish_rejects_a_second_rule_for_the_same_machine_and_normalized_extension()
        {
            string databasePath = NewDatabasePath();
            try
            {
                using (var store = OpenStore(databasePath))
                {
                    ParseRuleVersion first = Validate(store, store.SaveDraft(NewRule("rule-a", "A1")));
                    store.Publish(first.Id, "Machine1", first.Revision);

                    MappingRuleDefinition secondRule = NewRule("rule-b", "B2");
                    secondRule.NormalizedExtension = ".XLSX";
                    ParseRuleVersion second = Validate(store, store.SaveDraft(secondRule));

                    Assert.Throws<ParseRuleBindingConflictException>(() =>
                        store.Publish(second.Id, "Machine1", second.Revision));
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        [Fact]
        public void Publish_to_multiple_machines_is_atomic_when_any_binding_conflicts()
        {
            string databasePath = NewDatabasePath();
            try
            {
                using (var store = OpenStore(databasePath))
                {
                    ParseRuleVersion occupied = Validate(store, store.SaveDraft(NewRule("occupied", "A1")));
                    store.Publish(occupied.Id, "Machine2", occupied.Revision);

                    ParseRuleVersion candidate = Validate(store, store.SaveDraft(NewRule("candidate", "B2")));

                    Assert.Throws<ParseRuleBindingConflictException>(() =>
                        store.Publish(
                            candidate.Id,
                            new[] { "Machine1", "Machine2" },
                            candidate.Revision));

                    Assert.Null(store.GetPublished("Machine1", ".xlsx"));
                    Assert.Equal(occupied.Id, store.GetPublished("Machine2", ".xlsx").Id);
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        [Fact]
        public void Publish_does_not_overwrite_a_binding_that_changed_after_confirmation()
        {
            string databasePath = NewDatabasePath();
            try
            {
                using (var store = OpenStore(databasePath))
                {
                    ParseRuleVersion candidate = Validate(store, store.SaveDraft(NewRule("candidate", "A1")));
                    ParseRuleVersion competing = Validate(store, store.SaveDraft(NewRule("competing", "B2")));
                    ParseRuleVersion competingPublished = store.Publish(
                        competing.Id,
                        "Machine1",
                        competing.Revision);

                    Assert.Throws<ParseRuleBindingConflictException>(() =>
                        store.Publish(
                            candidate.Id,
                            new[] { "Machine1" },
                            candidate.Revision,
                            replaceExisting: true,
                            expectedExistingBindings: new Dictionary<string, long?>
                            {
                                { "Machine1", null }
                            }));

                    Assert.Equal(
                        competingPublished.Id,
                        store.GetPublished("Machine1", ".xlsx").Id);
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        [Fact]
        public void Validate_rejects_a_stale_revision()
        {
            string databasePath = NewDatabasePath();
            try
            {
                using (var store = OpenStore(databasePath))
                {
                    ParseRuleVersion draft = store.SaveDraft(NewRule("rule-a", "A1"));

                    Assert.Throws<ParseRuleRevisionConflictException>(() =>
                        store.Validate(draft.Id, draft.Revision + 1, "sample passed"));
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        [Fact]
        public void Rollback_copies_a_historical_published_version_into_a_new_published_version()
        {
            string databasePath = NewDatabasePath();
            try
            {
                using (var store = OpenStore(databasePath))
                {
                    ParseRuleVersion first = Validate(store, store.SaveDraft(NewRule("rule-a", "A1")));
                    ParseRuleVersion firstPublished = store.Publish(first.Id, "Machine1", first.Revision);

                    MappingRuleDefinition changedRule = NewRule("rule-a", "B2");
                    changedRule.DefinitionId = firstPublished.DefinitionId;
                    ParseRuleVersion changed = store.SaveDraft(changedRule, firstPublished.Revision);
                    changed = Validate(store, changed);
                    ParseRuleVersion secondPublished = store.Publish(
                        changed.Id,
                        "Machine1",
                        changed.Revision,
                        replaceExisting: true);

                    ParseRuleVersion rolledBack = store.Rollback(
                        firstPublished.Id,
                        "Machine1",
                        secondPublished.Id);

                    Assert.NotEqual(firstPublished.Id, rolledBack.Id);
                    Assert.NotEqual(secondPublished.Id, rolledBack.Id);
                    Assert.True(rolledBack.VersionNumber > secondPublished.VersionNumber);
                    Assert.Equal(firstPublished.DefinitionJson, rolledBack.DefinitionJson);
                    Assert.Equal(ParseRuleStatus.Published, rolledBack.Status);
                    Assert.Equal(rolledBack.Id, store.GetPublished("Machine1", ".xlsx").Id);
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        [Fact]
        public void Publish_rejects_a_model_rename_that_happened_after_validation()
        {
            string databasePath = NewDatabasePath();
            try
            {
                CreateModelSchema(databasePath);
                using (var store = OpenStore(databasePath))
                {
                    MappingRuleDefinition rule = NewRule("rule-a", "A1");
                    rule.ModelSchemaHash = new ModelSchemaService(
                        "Data Source=" + databasePath + ";Version=3;").ComputeHash(7);
                    ParseRuleVersion validated = Validate(store, store.SaveDraft(rule));

                    using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
                    using (var command = new SQLiteCommand(
                        "UPDATE DataModels SET ModelName='RenamedRecord' WHERE Id=7;",
                        connection))
                    {
                        connection.Open();
                        command.ExecuteNonQuery();
                    }

                    Assert.Throws<MappingValidationException>(() =>
                        store.Publish(validated.Id, "Machine1", validated.Revision));
                    Assert.Null(store.GetPublished("Machine1", ".xlsx"));
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        [Fact]
        public void Rollback_rejects_a_binding_that_changed_after_user_confirmation()
        {
            string databasePath = NewDatabasePath();
            try
            {
                using (var store = OpenStore(databasePath))
                {
                    ParseRuleVersion first = Validate(store, store.SaveDraft(NewRule("rule-a", "A1")));
                    ParseRuleVersion firstPublished = store.Publish(first.Id, "Machine1", first.Revision);

                    MappingRuleDefinition secondRule = NewRule("rule-a", "B2");
                    secondRule.DefinitionId = firstPublished.DefinitionId;
                    ParseRuleVersion second = Validate(
                        store,
                        store.SaveDraft(secondRule, firstPublished.Revision));
                    ParseRuleVersion confirmedCurrent = store.Publish(
                        second.Id,
                        "Machine1",
                        second.Revision,
                        replaceExisting: true);

                    MappingRuleDefinition thirdRule = NewRule("rule-a", "C3");
                    thirdRule.DefinitionId = firstPublished.DefinitionId;
                    ParseRuleVersion third = Validate(
                        store,
                        store.SaveDraft(thirdRule, confirmedCurrent.Revision));
                    ParseRuleVersion concurrentlyPublished = store.Publish(
                        third.Id,
                        "Machine1",
                        third.Revision,
                        replaceExisting: true);

                    Assert.Throws<ParseRuleBindingConflictException>(() => store.Rollback(
                        firstPublished.Id,
                        "Machine1",
                        confirmedCurrent.Id));
                    Assert.Equal(
                        concurrentlyPublished.Id,
                        store.GetPublished("Machine1", ".xlsx").Id);
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        [Fact]
        public void Validate_rejects_a_new_required_model_field_missing_from_the_saved_draft()
        {
            string databasePath = NewDatabasePath();
            try
            {
                CreateModelSchema(databasePath);
                using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
                using (var command = new SQLiteCommand(@"
INSERT INTO ModelFields
    (Id, ModelId, FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description)
VALUES
    (2, 7, 'MeasuredAt', 'datetime', 0, 1, 0, 0, 'required after UI load');", connection))
                {
                    connection.Open();
                    command.ExecuteNonQuery();
                }

                using (var store = OpenStore(databasePath))
                {
                    MappingRuleDefinition staleGridRule = NewRule("rule-a", "A1");
                    staleGridRule.ModelSchemaHash = new ModelSchemaService(
                        "Data Source=" + databasePath + ";Version=3;").ComputeHash(7);

                    ParseRuleVersion draft = store.SaveDraft(staleGridRule);
                    Assert.Throws<MappingValidationException>(() =>
                        store.Validate(draft.Id, draft.Revision, "sample passed"));
                }
            }
            finally
            {
                File.Delete(databasePath);
            }
        }

        private static ParseRuleStore OpenStore(string databasePath)
        {
            var store = new ParseRuleStore(databasePath);
            store.Initialize();
            return store;
        }

        private static ParseRuleVersion Validate(ParseRuleStore store, ParseRuleVersion draft)
        {
            return store.Validate(draft.Id, draft.Revision, "sample passed");
        }

        private static MappingRuleDefinition NewRule(string name, string cell)
        {
            return new MappingRuleDefinition
            {
                RuleName = name,
                ModelId = 7,
                TargetModelType = "InspectionRecord",
                ModelSchemaHash = "schema-v1",
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
                            Cell = cell,
                            AnchorCell = "A1",
                            AnchorText = "Template anchor"
                        },
                        Transforms = new List<string> { "trim" }
                    }
                }
            };
        }

        private static string NewDatabasePath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "ParseRuleStoreTests_" + Guid.NewGuid().ToString("N") + ".db");
        }

        private static void CreateModelSchema(string databasePath)
        {
            using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
CREATE TABLE DataModels (Id INTEGER PRIMARY KEY, ModelName TEXT NOT NULL, TableName TEXT NOT NULL);
CREATE TABLE ModelFields (
    Id INTEGER PRIMARY KEY,
    ModelId INTEGER NOT NULL,
    FieldName TEXT NOT NULL,
    FieldType TEXT NOT NULL,
    FieldLength INTEGER DEFAULT 0,
    IsRequired INTEGER DEFAULT 0,
    IsPrimaryKey INTEGER DEFAULT 0,
    IsIdentity INTEGER DEFAULT 0,
    Description TEXT);
INSERT INTO DataModels (Id, ModelName, TableName) VALUES (7, 'InspectionRecord', 'InspectionRecord');
INSERT INTO ModelFields
    (Id, ModelId, FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description)
VALUES
    (1, 7, 'SerialNumber', 'string', 64, 1, 0, 0, 'serial');";
                command.ExecuteNonQuery();
            }
        }
    }
}
