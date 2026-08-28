using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Core.Mapping;
using SqlSugar;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Persistence
{
    public sealed class MasterDetailPersistenceTests
    {
        [Fact]
        public async System.Threading.Tasks.Task PersistAsync_inserts_one_master_and_all_details_with_the_same_parent_cid()
        {
            string databasePath = CreateDatabasePath();
            try
            {
                using (var db = CreateClient(databasePath))
                {
                    long nextId = 100;
                    var aggregate = new MasterDetailParseResult(
                        new FileHeader { FileName = "设备A[批次01].xlsx" },
                        new object[]
                        {
                            new FileDetail { PointName = "P1" },
                            new FileDetail { PointName = "P2" }
                        },
                        1,
                        nameof(FileHeader),
                        2,
                        nameof(FileDetail),
                        "PARENT_CID");

                    MasterDetailPersistenceResult result = await new MasterDetailPersistenceService()
                        .PersistAsync(
                            db,
                            aggregate,
                            CreateMasterDefinition(),
                            CreateDetailDefinition(),
                            () => ++nextId,
                            CancellationToken.None);

                    Assert.Equal(101, result.MasterCid);
                    Assert.Equal(2, result.DetailCount);
                    Assert.Equal(1, db.Ado.GetInt("SELECT COUNT(1) FROM T_FILE_HEADER;"));
                    Assert.Equal(2, db.Ado.GetInt("SELECT COUNT(1) FROM T_FILE_DETAIL;"));
                    Assert.Equal(2, db.Ado.GetInt(
                        "SELECT COUNT(1) FROM T_FILE_DETAIL WHERE PARENT_CID=101;"));
                }
            }
            finally
            {
                if (File.Exists(databasePath)) File.Delete(databasePath);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task PersistAsync_rolls_back_the_master_when_any_detail_insert_fails()
        {
            string databasePath = CreateDatabasePath();
            try
            {
                using (var db = CreateClient(databasePath))
                {
                    long nextId = 200;
                    var aggregate = new MasterDetailParseResult(
                        new FileHeader { FileName = "bad.xlsx" },
                        new object[] { new FileDetail { PointName = null } },
                        1,
                        nameof(FileHeader),
                        2,
                        nameof(FileDetail),
                        "PARENT_CID");

                    await Assert.ThrowsAnyAsync<Exception>(() => new MasterDetailPersistenceService()
                        .PersistAsync(
                            db,
                            aggregate,
                            CreateMasterDefinition(),
                            CreateDetailDefinition(),
                            () => ++nextId,
                            CancellationToken.None));

                    Assert.Equal(0, db.Ado.GetInt("SELECT COUNT(1) FROM T_FILE_HEADER;"));
                    Assert.Equal(0, db.Ado.GetInt("SELECT COUNT(1) FROM T_FILE_DETAIL;"));
                }
            }
            finally
            {
                if (File.Exists(databasePath)) File.Delete(databasePath);
            }
        }

        [Fact]
        public void PersistAsync_commits_the_master_detail_transaction_on_a_real_local_sql_server()
        {
            TargetTableProvisionerTests.RunWithLocalSqlServer(db =>
            {
                long nextId = 300;
                var aggregate = new MasterDetailParseResult(
                    new FileHeader { FileName = "sqlserver[01].xlsx" },
                    new object[]
                    {
                        new FileDetail { PointName = "P1" },
                        new FileDetail { PointName = "P2" }
                    },
                    1,
                    nameof(FileHeader),
                    2,
                    nameof(FileDetail),
                    "PARENT_CID");

                MasterDetailPersistenceResult result = new MasterDetailPersistenceService()
                    .PersistAsync(
                        db,
                        aggregate,
                        CreateMasterDefinition(),
                        CreateDetailDefinition(),
                        () => ++nextId,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Assert.Equal(301, result.MasterCid);
                Assert.Equal(1, db.Ado.GetInt("SELECT COUNT(1) FROM [T_FILE_HEADER];"));
                Assert.Equal(2, db.Ado.GetInt(
                    "SELECT COUNT(1) FROM [T_FILE_DETAIL] WHERE [PARENT_CID]=301;"));
                Assert.Equal(1, db.Ado.GetInt(
                    "SELECT COUNT(1) FROM sys.indexes WHERE name='IX_T_FILE_DETAIL_PARENT_CID';"));
            });
        }

        [Fact]
        public async System.Threading.Tasks.Task PersistAsync_does_not_insert_when_cancelled_before_the_transaction()
        {
            string databasePath = CreateDatabasePath();
            try
            {
                using (var db = CreateClient(databasePath))
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    var aggregate = new MasterDetailParseResult(
                        new FileHeader { FileName = "cancel.xlsx" },
                        new object[] { new FileDetail { PointName = "P1" } },
                        1,
                        nameof(FileHeader),
                        2,
                        nameof(FileDetail),
                        "PARENT_CID");

                    await Assert.ThrowsAsync<OperationCanceledException>(() =>
                        new MasterDetailPersistenceService().PersistAsync(
                            db,
                            aggregate,
                            CreateMasterDefinition(),
                            CreateDetailDefinition(),
                            () => 1,
                            cancellation.Token));
                    Assert.False(db.DbMaintenance.IsAnyTable("T_FILE_HEADER", false));
                    Assert.False(db.DbMaintenance.IsAnyTable("T_FILE_DETAIL", false));
                }
            }
            finally
            {
                if (File.Exists(databasePath)) File.Delete(databasePath);
            }
        }

        private static string CreateDatabasePath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "MasterDetailPersistence_" + Guid.NewGuid().ToString("N") + ".db");
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

        private static TargetTableDefinition CreateMasterDefinition()
        {
            return new TargetTableDefinition
            {
                TableName = "T_FILE_HEADER",
                Columns = new[]
                {
                    RequiredLong("CID", true),
                    new TargetTableColumnDefinition
                    {
                        FieldName = "FileName",
                        FieldType = "string",
                        FieldLength = 260,
                        IsRequired = true
                    }
                }
            };
        }

        private static TargetTableDefinition CreateDetailDefinition()
        {
            return new TargetTableDefinition
            {
                TableName = "T_FILE_DETAIL",
                Columns = new[]
                {
                    RequiredLong("CID", true),
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
                        FieldLength = 40,
                        IsRequired = true
                    }
                }
            };
        }

        private static TargetTableColumnDefinition RequiredLong(string name, bool primaryKey)
        {
            return new TargetTableColumnDefinition
            {
                FieldName = name,
                FieldType = "long",
                IsRequired = true,
                IsPrimaryKey = primaryKey
            };
        }

        [SugarTable("T_FILE_HEADER")]
        public sealed class FileHeader
        {
            public long CID { get; set; }
            public string FileName { get; set; }
        }

        [SugarTable("T_FILE_DETAIL")]
        public sealed class FileDetail
        {
            public long CID { get; set; }
            public long? PARENT_CID { get; set; }
            public string PointName { get; set; }
        }
    }
}
