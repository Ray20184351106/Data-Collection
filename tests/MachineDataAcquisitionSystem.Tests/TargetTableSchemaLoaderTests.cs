using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Persistence
{
    public sealed class TargetTableSchemaLoaderTests
    {
        [Fact]
        public void Loader_preserves_base_and_model_field_database_configuration()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "TargetTableSchema_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = @"
CREATE TABLE DataModels (Id INTEGER PRIMARY KEY,TableName TEXT,ParentModelId INTEGER,IsActive INTEGER);
CREATE TABLE BaseFields (Id INTEGER PRIMARY KEY,FieldName TEXT,FieldType TEXT,FieldLength INTEGER,IsRequired INTEGER,SortOrder INTEGER,Description TEXT);
CREATE TABLE ModelFields (Id INTEGER PRIMARY KEY,ModelId INTEGER,FieldName TEXT,FieldType TEXT,FieldLength INTEGER,IsRequired INTEGER,IsPrimaryKey INTEGER,IsIdentity INTEGER,Description TEXT);
INSERT INTO DataModels VALUES (6,'TBL_GMJL',-2,1);
INSERT INTO BaseFields VALUES (1,'CID','long',0,1,1,'id');
INSERT INTO BaseFields VALUES (2,'CROWREMARK','string',200,0,2,'remark');
INSERT INTO ModelFields VALUES (1,6,'bs','string',40,0,0,0,'label');
INSERT INTO ModelFields VALUES (2,6,'SequenceId','int',0,1,1,1,'sequence');";
                    command.ExecuteNonQuery();
                }

                TargetTableDefinition definition = new TargetTableSchemaLoader(
                    "Data Source=" + databasePath + ";Version=3;").Load(6);

                Assert.Equal("TBL_GMJL", definition.TableName);
                Assert.False(definition.Columns.Single(column => column.FieldName == "CROWREMARK").IsRequired);
                Assert.Equal(200, definition.Columns.Single(column => column.FieldName == "CROWREMARK").FieldLength);
                Assert.False(definition.Columns.Single(column => column.FieldName == "bs").IsRequired);
                TargetTableColumnDefinition sequence = definition.Columns.Single(
                    column => column.FieldName == "SequenceId");
                Assert.True(sequence.IsRequired);
                Assert.True(sequence.IsPrimaryKey);
                Assert.True(sequence.IsIdentity);
            }
            finally
            {
                if (File.Exists(databasePath)) File.Delete(databasePath);
            }
        }
    }
}
