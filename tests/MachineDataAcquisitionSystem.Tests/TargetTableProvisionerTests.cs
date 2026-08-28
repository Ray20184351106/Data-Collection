using System;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using MachineDataAcquisitionSystem.Core;
using SqlSugar;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Persistence
{
    public sealed class TargetTableProvisionerTests
    {
        [Fact]
        public void EnsureTable_creates_a_missing_table_and_allows_the_parsed_object_to_be_inserted()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "TargetTableProvisioner_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var db = CreateClient(databasePath))
                {
                    TargetTableProvisionResult result = new TargetTableProvisioner()
                        .EnsureTable(db, typeof(ProvisionedInspection), CreateDefinition());

                    Assert.True(result.Created);
                    Assert.Equal("AutoCreatedInspection", result.TableName);
                    Assert.True(db.DbMaintenance.IsAnyTable(result.TableName, false));

                    int inserted = db.InsertableByObject(new ProvisionedInspection
                    {
                        CID = 1001,
                        LotNumber = "LOT-001",
                        RequiredCode = "REQ"
                    }).ExecuteCommand();
                    Assert.Equal(1, inserted);
                }

                using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = "SELECT LotNumber FROM AutoCreatedInspection WHERE CID=1001;";
                    Assert.Equal("LOT-001", Convert.ToString(command.ExecuteScalar()));
                }
            }
            finally
            {
                if (File.Exists(databasePath)) File.Delete(databasePath);
            }
        }

        [Fact]
        public void EnsureTable_leaves_an_existing_table_in_place()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "TargetTableProvisioner_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var db = CreateClient(databasePath))
                {
                    var provisioner = new TargetTableProvisioner();
                    TargetTableProvisionResult first = provisioner.EnsureTable(
                        db,
                        typeof(ProvisionedInspection),
                        CreateDefinition());
                    TargetTableProvisionResult second = provisioner.EnsureTable(
                        db,
                        typeof(ProvisionedInspection),
                        CreateDefinition());

                    Assert.True(first.Created);
                    Assert.False(second.Created);
                    Assert.Equal(first.TableName, second.TableName);
                }
            }
            finally
            {
                if (File.Exists(databasePath)) File.Delete(databasePath);
            }
        }

        [Fact]
        public void EnsureTable_adds_the_system_parent_column_and_index_idempotently()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "TargetTableProvisioner_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = @"
CREATE TABLE AutoCreatedDetail (
    CID INTEGER NOT NULL PRIMARY KEY,
    PointName VARCHAR(40) NULL);";
                    command.ExecuteNonQuery();
                }

                using (var db = CreateClient(databasePath))
                {
                    var provisioner = new TargetTableProvisioner();
                    TargetTableProvisionResult first = provisioner.EnsureTable(
                        db,
                        typeof(ProvisionedDetail),
                        CreateDetailDefinition());
                    TargetTableProvisionResult second = provisioner.EnsureTable(
                        db,
                        typeof(ProvisionedDetail),
                        CreateDetailDefinition());

                    Assert.False(first.Created);
                    Assert.Equal(1, first.AdjustedColumnCount);
                    Assert.Equal(0, second.AdjustedColumnCount);
                    Assert.Contains(
                        db.DbMaintenance.GetColumnInfosByTableName("AutoCreatedDetail", false),
                        column => string.Equals(column.DbColumnName, "PARENT_CID", StringComparison.OrdinalIgnoreCase));
                    Assert.Equal(1, db.Ado.GetInt(
                        "SELECT COUNT(1) FROM sqlite_master " +
                        "WHERE type='index' AND name='IX_AutoCreatedDetail_PARENT_CID';"));
                }
            }
            finally
            {
                if (File.Exists(databasePath)) File.Delete(databasePath);
            }
        }

        [Fact]
        public void EnsureTable_refuses_to_rebuild_an_existing_sqlite_table_to_change_nullability()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "TargetTableProvisioner_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = @"
CREATE TABLE AutoCreatedInspection (
    CID INTEGER NOT NULL PRIMARY KEY,
    LotNumber VARCHAR(40) NOT NULL,
    RequiredCode VARCHAR(20) NOT NULL);";
                    command.ExecuteNonQuery();
                }

                using (var db = CreateClient(databasePath))
                {
                    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                        new TargetTableProvisioner().EnsureTable(
                            db,
                            typeof(ProvisionedInspection),
                            CreateDefinition()));

                    Assert.Contains("SQLite", exception.Message);
                }
            }
            finally
            {
                if (File.Exists(databasePath)) File.Delete(databasePath);
            }
        }

        [Fact]
        public void EnsureTable_rejects_a_runtime_type_without_an_approved_table_mapping()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "TargetTableProvisioner_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var db = CreateClient(databasePath))
                {
                    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                        new TargetTableProvisioner().EnsureTable(
                            db,
                            typeof(UnmappedInspection),
                            CreateDefinition()));

                    Assert.Contains("禁止自动创建", exception.Message);
                    Assert.False(db.DbMaintenance.IsAnyTable(nameof(UnmappedInspection), false));
                }
            }
            finally
            {
                if (File.Exists(databasePath)) File.Delete(databasePath);
            }
        }

        [Fact]
        public void EnsureTable_creates_and_inserts_on_a_real_local_sql_server()
        {
            RunWithLocalSqlServer(db =>
            {
                TargetTableProvisionResult result = new TargetTableProvisioner()
                    .EnsureTable(db, typeof(ProvisionedInspection), CreateDefinition());
                int inserted = db.InsertableByObject(new ProvisionedInspection
                {
                    CID = 2001,
                    LotNumber = "SQLSERVER-001",
                    RequiredCode = "REQ"
                }).ExecuteCommand();

                Assert.True(result.Created);
                Assert.Equal(1, inserted);
                Assert.Equal(1, db.Ado.GetInt(
                    "SELECT COUNT(1) FROM [AutoCreatedInspection] WHERE [CID]=2001"));
                Assert.Equal("YES", db.Ado.GetString(
                    "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE TABLE_NAME='AutoCreatedInspection' AND COLUMN_NAME='LotNumber'"));
                Assert.Equal(40, db.Ado.GetInt(
                    "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE TABLE_NAME='AutoCreatedInspection' AND COLUMN_NAME='LotNumber'"));
                Assert.Equal("NO", db.Ado.GetString(
                    "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE TABLE_NAME='AutoCreatedInspection' AND COLUMN_NAME='RequiredCode'"));
                Assert.Equal(1, db.Ado.GetInt(
                    "SELECT COUNT(1) FROM sys.indexes " +
                    "WHERE object_id=OBJECT_ID(N'AutoCreatedInspection') AND is_primary_key=1"));
            });
        }

        [Fact]
        public void EnsureTable_relaxes_legacy_not_null_optional_columns_on_sql_server()
        {
            RunWithLocalSqlServer(db =>
            {
                db.Ado.ExecuteCommand(@"
CREATE TABLE [AutoCreatedInspection] (
    [CID] BIGINT NOT NULL PRIMARY KEY,
    [LotNumber] VARCHAR(40) NOT NULL,
    [RequiredCode] VARCHAR(20) NOT NULL);");

                TargetTableProvisionResult result = new TargetTableProvisioner().EnsureTable(
                    db,
                    typeof(ProvisionedInspection),
                    CreateDefinition());

                Assert.False(result.Created);
                Assert.Equal(1, result.AdjustedColumnCount);
                Assert.Equal("YES", db.Ado.GetString(
                    "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE TABLE_NAME='AutoCreatedInspection' AND COLUMN_NAME='LotNumber'"));
            });
        }

        private static SqlSugarClient CreateClient(string databasePath)
        {
            return new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=" + databasePath + ";Version=3;",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true
            });
        }

        internal static void RunWithLocalSqlServer(Action<SqlSugarClient> assertion)
        {
            string masterConnectionString = Environment.GetEnvironmentVariable(
                "TARGET_TABLE_SQLSERVER_TEST_MASTER");
            if (string.IsNullOrWhiteSpace(masterConnectionString)) return;

            string databaseName = "CodexTargetTable_" + Guid.NewGuid().ToString("N");
            var masterBuilder = new SqlConnectionStringBuilder(masterConnectionString);
            string escapedDatabaseName = databaseName.Replace("]", "]]");
            using (var connection = new SqlConnection(masterBuilder.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = "CREATE DATABASE [" + escapedDatabaseName + "];";
                command.ExecuteNonQuery();
            }

            try
            {
                var databaseBuilder = new SqlConnectionStringBuilder(masterBuilder.ConnectionString)
                {
                    InitialCatalog = databaseName
                };
                using (var db = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = databaseBuilder.ConnectionString,
                    DbType = DbType.SqlServer,
                    IsAutoCloseConnection = true
                }))
                {
                    assertion(db);
                }
            }
            finally
            {
                SqlConnection.ClearAllPools();
                using (var connection = new SqlConnection(masterBuilder.ConnectionString))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText =
                        "ALTER DATABASE [" + escapedDatabaseName + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" +
                        "DROP DATABASE [" + escapedDatabaseName + "];";
                    command.ExecuteNonQuery();
                }
            }
        }

        private static TargetTableDefinition CreateDefinition()
        {
            return new TargetTableDefinition
            {
                TableName = "AutoCreatedInspection",
                Columns = new[]
                {
                    new TargetTableColumnDefinition
                    {
                        FieldName = "CID",
                        FieldType = "long",
                        IsRequired = true,
                        IsPrimaryKey = true
                    },
                    new TargetTableColumnDefinition
                    {
                        FieldName = "LotNumber",
                        FieldType = "string",
                        FieldLength = 40,
                        IsRequired = false
                    },
                    new TargetTableColumnDefinition
                    {
                        FieldName = "RequiredCode",
                        FieldType = "string",
                        FieldLength = 20,
                        IsRequired = true
                    }
                }
            };
        }

        private static TargetTableDefinition CreateDetailDefinition()
        {
            return new TargetTableDefinition
            {
                TableName = "AutoCreatedDetail",
                Columns = new[]
                {
                    new TargetTableColumnDefinition
                    {
                        FieldName = "CID",
                        FieldType = "long",
                        IsRequired = true,
                        IsPrimaryKey = true
                    },
                    new TargetTableColumnDefinition
                    {
                        FieldName = "PARENT_CID",
                        FieldType = "long",
                        IsSystemGenerated = true,
                        SystemRole = "ParentCid"
                    },
                    new TargetTableColumnDefinition
                    {
                        FieldName = "PointName",
                        FieldType = "string",
                        FieldLength = 40
                    }
                }
            };
        }

        [SugarTable("AutoCreatedInspection")]
        public sealed class ProvisionedInspection
        {
            public long CID { get; set; }
            public string LotNumber { get; set; }
            public string RequiredCode { get; set; }
        }

        public sealed class UnmappedInspection
        {
            public string Value { get; set; }
        }

        [SugarTable("AutoCreatedDetail")]
        public sealed class ProvisionedDetail
        {
            public long CID { get; set; }
            public long? PARENT_CID { get; set; }
            public string PointName { get; set; }
        }
    }
}
