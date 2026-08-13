using System;
using System.Data.SQLite;
using System.IO;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class ModelCopyServiceTests
    {
        [Fact]
        public void CopyModel_copies_model_and_fields_as_an_inactive_independent_model()
        {
            using (var fixture = ModelCopyFixture.Create())
            {
                ModelCopyResult copy = new ModelCopyService(fixture.DatabasePath).CopyModel(7);

                Assert.NotEqual(7, copy.ModelId);
                Assert.Equal("InspectionRecord_Copy", copy.ModelName);
                Assert.Equal("InspectionRecord_Copy", copy.TableName);
                Assert.Equal("InspectionRecord_Copy|InspectionRecord_Copy|3|inspection|0", fixture.ScalarText(
                    "SELECT ModelName||'|'||TableName||'|'||ParentModelId||'|'||Description||'|'||IsActive FROM DataModels WHERE Id=" + copy.ModelId + ";"));
                Assert.Equal(2L, fixture.ScalarLong(
                    "SELECT COUNT(*) FROM ModelFields WHERE ModelId=" + copy.ModelId + ";"));
                Assert.Equal(0L, fixture.ScalarLong(
                    "SELECT COUNT(*) FROM ParseScripts WHERE ModelId=" + copy.ModelId + ";"));
                Assert.Equal(0L, fixture.ScalarLong(
                    "SELECT COUNT(*) FROM ParseRuleDefinitions WHERE ModelId=" + copy.ModelId + ";"));

                fixture.Execute("UPDATE ModelFields SET FieldLength=128 WHERE ModelId=" + copy.ModelId + " AND FieldName='SerialNumber';");

                Assert.Equal(64L, fixture.ScalarLong(
                    "SELECT FieldLength FROM ModelFields WHERE ModelId=7 AND FieldName='SerialNumber';"));
            }
        }

        [Fact]
        public void CopyModel_uses_the_next_available_model_and_table_names()
        {
            using (var fixture = ModelCopyFixture.Create())
            {
                fixture.Execute("INSERT INTO DataModels (Id,ModelName,TableName,ParentModelId,Description,IsActive) VALUES (8,'InspectionRecord_Copy','InspectionRecord_Copy',0,'existing',0);");

                ModelCopyResult copy = new ModelCopyService(fixture.DatabasePath).CopyModel(7);

                Assert.Equal("InspectionRecord_Copy2", copy.ModelName);
                Assert.Equal("InspectionRecord_Copy2", copy.TableName);
            }
        }

        private sealed class ModelCopyFixture : IDisposable
        {
            public string DatabasePath { get; private set; }

            public static ModelCopyFixture Create()
            {
                var fixture = new ModelCopyFixture
                {
                    DatabasePath = Path.Combine(Path.GetTempPath(), "ModelCopy_" + Guid.NewGuid().ToString("N") + ".db")
                };
                fixture.Execute(@"
CREATE TABLE DataModels (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ModelName TEXT NOT NULL,
    TableName TEXT NOT NULL,
    ParentModelId INTEGER DEFAULT 0,
    Description TEXT,
    IsActive INTEGER DEFAULT 1
);
CREATE TABLE ModelFields (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ModelId INTEGER NOT NULL,
    FieldName TEXT NOT NULL,
    FieldType TEXT NOT NULL,
    FieldLength INTEGER DEFAULT 0,
    IsRequired INTEGER DEFAULT 0,
    IsPrimaryKey INTEGER DEFAULT 0,
    IsIdentity INTEGER DEFAULT 0,
    Description TEXT
);
CREATE TABLE ParseScripts (Id INTEGER PRIMARY KEY, ModelId INTEGER NOT NULL);
CREATE TABLE ParseRuleDefinitions (Id INTEGER PRIMARY KEY, ModelId INTEGER NOT NULL);
INSERT INTO DataModels (Id,ModelName,TableName,ParentModelId,Description,IsActive)
VALUES (7,'InspectionRecord','InspectionRecord',3,'inspection',1);
INSERT INTO ModelFields (ModelId,FieldName,FieldType,FieldLength,IsRequired,IsPrimaryKey,IsIdentity,Description) VALUES
(7,'SerialNumber','string',64,1,0,0,'serial'),
(7,'Result','bool',0,0,0,0,'result');
INSERT INTO ParseScripts (Id,ModelId) VALUES (1,7);
INSERT INTO ParseRuleDefinitions (Id,ModelId) VALUES (1,7);");
                return fixture;
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

            public string ScalarText(string sql)
            {
                using (var connection = new SQLiteConnection("Data Source=" + DatabasePath + ";Version=3;"))
                using (var command = new SQLiteCommand(sql, connection))
                {
                    connection.Open();
                    return Convert.ToString(command.ExecuteScalar());
                }
            }

            public void Dispose()
            {
                if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
            }
        }
    }
}
