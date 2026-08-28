using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class MasterDetailConfigurationTests
    {
        [Fact]
        public void Ensure_relation_field_creates_an_optional_system_long_field_and_is_idempotent()
        {
            string path = CreateDatabase();
            try
            {
                var service = new MasterDetailConfigurationService(Connection(path));

                RelationFieldProvisionResult first = service.EnsureRelationField(2, "PARENT_CID");
                RelationFieldProvisionResult second = service.EnsureRelationField(2, "PARENT_CID");

                Assert.True(first.Created);
                Assert.False(second.Created);
                using (var connection = new SQLiteConnection(Connection(path)))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = @"
SELECT FieldType,IsRequired,IsPrimaryKey,IsIdentity,IsSystemGenerated,SystemRole
FROM ModelFields WHERE ModelId=2 AND FieldName='PARENT_CID';";
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        Assert.True(reader.Read());
                        Assert.Equal("long", reader.GetString(0));
                        Assert.Equal(0, reader.GetInt32(1));
                        Assert.Equal(0, reader.GetInt32(2));
                        Assert.Equal(0, reader.GetInt32(3));
                        Assert.Equal(1, reader.GetInt32(4));
                        Assert.Equal("ParentCid", reader.GetString(5));
                    }
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Ensure_relation_field_rejects_an_incompatible_existing_field()
        {
            string path = CreateDatabase();
            try
            {
                Execute(path, @"
INSERT INTO ModelFields
    (ModelId,FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,Description)
VALUES (2,'PARENT_CID','string',50,0,0,0,'wrong');");

                MappingValidationException error = Assert.Throws<MappingValidationException>(() =>
                    new MasterDetailConfigurationService(Connection(path))
                        .EnsureRelationField(2, "PARENT_CID"));

                Assert.Contains("long", error.Message);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Ensure_relation_field_blocks_mutating_a_model_used_by_a_published_rule()
        {
            string path = CreateDatabase();
            try
            {
                using (var store = new ParseRuleStore(path))
                    store.Initialize();
                Execute(path, @"
INSERT INTO ParseRuleDefinitions
    (Id,RuleName,ModelId,TargetModelType,NormalizedExtension,CreatedTime,UpdatedTime)
VALUES (1,'published',2,'DetailModel','.xlsx',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
INSERT INTO ParseRuleVersions
    (Id,DefinitionId,VersionNumber,Revision,RuleType,Status,DefinitionJson,
     DerivedScriptCode,ContentSha256,ModelSchemaHash,CreatedTime,PublishedTime)
VALUES (1,1,1,1,1,2,'{}','return null;','hash','schema',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
INSERT INTO PublishedParseRuleBindings
    (MachineId,NormalizedExtension,ParseRuleVersionId,PublishedTime)
VALUES ('1','.xlsx',1,CURRENT_TIMESTAMP);");

                ParseRuleStateException error = Assert.Throws<ParseRuleStateException>(() =>
                    new MasterDetailConfigurationService(Connection(path))
                        .EnsureRelationField(2, "PARENT_CID"));

                Assert.Contains("复制", error.Message);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Save_draft_rejects_master_detail_models_without_a_compatible_cid()
        {
            string path = CreateDatabase();
            try
            {
                Execute(path, @"
DELETE FROM BaseFields WHERE FieldName='CID';
INSERT INTO ModelFields
    (ModelId,FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,Description)
VALUES (1,'FileName','string',260,1,0,0,'file'),
       (2,'Point','string',50,1,0,0,'point'),
       (2,'PARENT_CID','long',0,0,0,0,'parent');");
                var schema = new ModelSchemaService(Connection(path));
                MappingRuleDefinition rule = CreateMasterDetailRule(
                    "HeaderModel",
                    "DetailModel",
                    schema.ComputeHash(1),
                    schema.ComputeHash(2));

                using (var store = new ParseRuleStore(path))
                {
                    store.Initialize();
                    MappingValidationException error = Assert.Throws<MappingValidationException>(() =>
                        store.SaveDraft(rule));
                    Assert.Contains("CID", error.Message);
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Save_draft_persists_immutable_main_and_detail_model_dependencies()
        {
            string path = CreateDatabase();
            string suffix = Guid.NewGuid().ToString("N");
            string mainType = "Header" + suffix;
            string detailType = "Detail" + suffix;
            string generatedDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeneratedModels");
            string mainPath = Path.Combine(generatedDirectory, mainType + ".cs");
            string detailPath = Path.Combine(generatedDirectory, detailType + ".cs");
            Directory.CreateDirectory(generatedDirectory);
            try
            {
                Execute(path, @"
UPDATE DataModels SET ModelName='" + mainType + @"' WHERE Id=1;
UPDATE DataModels SET ModelName='" + detailType + @"' WHERE Id=2;
INSERT INTO ModelFields
    (ModelId,FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,Description)
VALUES (1,'FileName','string',260,1,0,0,'file'),
       (2,'Point','string',50,1,0,0,'point'),
       (2,'PARENT_CID','long',0,0,0,0,'parent');");
                File.WriteAllText(mainPath,
                    "public class " + mainType + " { public long CID { get; set; } public string FileName { get; set; } }");
                File.WriteAllText(detailPath,
                    "public class " + detailType + " { public long CID { get; set; } public string Point { get; set; } public long? PARENT_CID { get; set; } }");

                var schema = new ModelSchemaService(Connection(path));
                MappingRuleDefinition rule = CreateMasterDetailRule(
                    mainType,
                    detailType,
                    schema.ComputeHash(1),
                    schema.ComputeHash(2));
                ParseRuleVersion draft;
                using (var store = new ParseRuleStore(path))
                {
                    store.Initialize();
                    draft = store.SaveDraft(rule);
                }

                using (var connection = new SQLiteConnection(Connection(path)))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = @"
SELECT Role,ModelId,ModelType,ModelSchemaHash,GeneratedModelCodeSnapshot,GeneratedModelCodeSha256
FROM ParseRuleVersionModels
WHERE ParseRuleVersionId=@VersionId
ORDER BY Role;";
                    command.Parameters.AddWithValue("@VersionId", draft.Id);
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        Assert.True(reader.Read());
                        Assert.Equal("Detail", reader.GetString(0));
                        Assert.Equal(2, reader.GetInt32(1));
                        Assert.Equal(detailType, reader.GetString(2));
                        Assert.False(string.IsNullOrWhiteSpace(reader.GetString(4)));
                        Assert.Equal(
                            MappingRuleSerializer.Sha256(reader.GetString(4)),
                            reader.GetString(5));
                        Assert.True(reader.Read());
                        Assert.Equal("Master", reader.GetString(0));
                        Assert.Equal(1, reader.GetInt32(1));
                        Assert.Equal(mainType, reader.GetString(2));
                        Assert.False(reader.Read());
                    }
                }
            }
            finally
            {
                if (File.Exists(mainPath)) File.Delete(mainPath);
                if (File.Exists(detailPath)) File.Delete(detailPath);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static MappingRuleDefinition CreateMasterDetailRule(
            string mainType,
            string detailType,
            string mainHash,
            string detailHash)
        {
            return new MappingRuleDefinition
            {
                RuleName = "aggregate",
                ModelId = 1,
                TargetModelType = mainType,
                ModelSchemaHash = mainHash,
                NormalizedExtension = ".xlsx",
                SheetName = "Data",
                RecordMode = MappingRecordMode.MasterDetail,
                MasterDetail = new MasterDetailMappingDefinition
                {
                    ParentCidField = "PARENT_CID",
                    FileName = new FileNameExtractionDefinition { ExpectedSegmentCount = 1 },
                    Master = new MappingTargetDefinition
                    {
                        ModelId = 1,
                        TargetModelType = mainType,
                        ModelSchemaHash = mainHash,
                        Fields =
                        {
                            new FieldMappingRule
                            {
                                TargetField = "FileName",
                                TargetType = "string",
                                IsRequired = true,
                                Locator = new MappingLocator { Type = "fileNameFull" }
                            }
                        }
                    },
                    Detail = new MappingTargetDefinition
                    {
                        ModelId = 2,
                        TargetModelType = detailType,
                        ModelSchemaHash = detailHash,
                        RepeatedRows = new RepeatedRowDefinition
                        {
                            AnchorMode = MappingTableAnchorMode.FixedCell,
                            AnchorCell = "A1",
                            FirstDataRowOffset = 1,
                            KeyColumnOffset = 0,
                            FirstColumnOffset = 0,
                            LastColumnOffset = 0,
                            StopOnBlankKey = true
                        },
                        Fields =
                        {
                            new FieldMappingRule
                            {
                                TargetField = "Point",
                                TargetType = "string",
                                IsRequired = true,
                                Scope = MappingFieldScope.RowColumn,
                                Locator = new MappingLocator
                                {
                                    Type = "rowColumn",
                                    Text = "Point",
                                    ColumnOffset = 0
                                }
                            }
                        }
                    }
                }
            };
        }

        private static string CreateDatabase()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "MasterDetailConfiguration_" + Guid.NewGuid().ToString("N") + ".db");
            Execute(path, @"
CREATE TABLE DataModels
(
    Id INTEGER PRIMARY KEY,
    ModelName TEXT NOT NULL,
    TableName TEXT NOT NULL,
    ParentModelId INTEGER DEFAULT 0,
    Description TEXT,
    IsActive INTEGER DEFAULT 1
);
CREATE TABLE ModelFields
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ModelId INTEGER NOT NULL,
    FieldName TEXT NOT NULL,
    FieldType TEXT NOT NULL,
    FieldLength INTEGER DEFAULT 0,
    IsRequired INTEGER DEFAULT 0,
    IsPrimaryKey INTEGER DEFAULT 0,
    IsIdentity INTEGER DEFAULT 0,
    Description TEXT,
    IsSystemGenerated INTEGER DEFAULT 0,
    SystemRole TEXT
);
CREATE TABLE BaseFields
(
    Id INTEGER PRIMARY KEY,
    FieldName TEXT NOT NULL,
    FieldType TEXT NOT NULL,
    FieldLength INTEGER DEFAULT 0,
    IsRequired INTEGER DEFAULT 0,
    DefaultValue TEXT,
    SortOrder INTEGER DEFAULT 0,
    Description TEXT
);
INSERT INTO BaseFields VALUES (1,'CID','long',0,1,'YitIdHelper.NextId()',1,'id');
INSERT INTO DataModels VALUES (1,'HeaderModel','T_HEADER',-2,'',1);
INSERT INTO DataModels VALUES (2,'DetailModel','T_DETAIL',-2,'',1);");
            return path;
        }

        private static void Execute(string path, string sql)
        {
            using (var connection = new SQLiteConnection(Connection(path)))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static string Connection(string path)
        {
            return "Data Source=" + path + ";Version=3;Foreign Keys=True;";
        }
    }
}
