using System;
using System.Collections.Generic;
using SqlSugar;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class RepeatingRowSqlServerIntegrationTests
    {
        private const string ConnectionString =
            @"Server=.\SQLEXPRESS;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5";

        [Fact]
        [Trait("Category", "Integration")]
        public void Repeating_row_batch_inserts_every_record_in_one_real_sql_transaction()
        {
            using (SqlSugarClient db = CreateClient())
            {
                CreateProbeTable(db);
                db.Ado.BeginTran();
                try
                {
                    var rows = new List<RepeatingRowProbe>
                    {
                        new RepeatingRowProbe { Id = 1, Point = "D1", MeasuredValue = 314.395m },
                        new RepeatingRowProbe { Id = 2, Point = "D2", MeasuredValue = 175.006m },
                        new RepeatingRowProbe { Id = 3, Point = "Position1", MeasuredValue = 0.016m }
                    };

                    int inserted = db.Insertable(rows).AS("#RepeatingRowProbe").ExecuteCommand();

                    Assert.Equal(rows.Count, inserted);
                    Assert.Equal(rows.Count, db.Ado.GetInt("SELECT COUNT(*) FROM #RepeatingRowProbe"));
                }
                finally
                {
                    db.Ado.RollbackTran();
                }
                Assert.Equal(0, db.Ado.GetInt("SELECT COUNT(*) FROM #RepeatingRowProbe"));
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void Repeating_row_batch_failure_leaves_zero_rows_in_the_real_sql_transaction()
        {
            using (SqlSugarClient db = CreateClient())
            {
                CreateProbeTable(db);
                db.Ado.BeginTran();
                try
                {
                    var rows = new List<RepeatingRowProbe>
                    {
                        new RepeatingRowProbe { Id = 1, Point = "D1", MeasuredValue = 314.395m },
                        new RepeatingRowProbe { Id = 2, Point = null, MeasuredValue = 175.006m }
                    };

                    Assert.ThrowsAny<Exception>(() =>
                        db.Insertable(rows).AS("#RepeatingRowProbe").ExecuteCommand());
                }
                finally
                {
                    db.Ado.RollbackTran();
                }
                Assert.Equal(0, db.Ado.GetInt("SELECT COUNT(*) FROM #RepeatingRowProbe"));
            }
        }

        private static SqlSugarClient CreateClient()
        {
            return new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = ConnectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = false
            });
        }

        private static void CreateProbeTable(SqlSugarClient db)
        {
            db.Ado.ExecuteCommand(@"
CREATE TABLE #RepeatingRowProbe
(
    Id int NOT NULL,
    Point nvarchar(50) NOT NULL,
    MeasuredValue decimal(18, 6) NOT NULL
);");
        }

        private sealed class RepeatingRowProbe
        {
            public int Id { get; set; }
            public string Point { get; set; }
            public decimal MeasuredValue { get; set; }
        }
    }
}
