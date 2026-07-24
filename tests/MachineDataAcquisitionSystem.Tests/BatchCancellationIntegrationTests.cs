using System;
using System.Threading;
using System.Threading.Tasks;
using SqlSugar;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public class BatchCancellationIntegrationTests
    {
        private sealed class PauseCancellationProbe
        {
            public int Id { get; set; }
            public string Value { get; set; }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task Cancelled_sqlsugar_batch_is_not_inserted_into_real_sql_server()
        {
            using (var db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = @"Server=.\SQLEXPRESS;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5",
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = false
            }))
            using (var cancellation = new CancellationTokenSource())
            {
                db.Ado.BeginTran();
                try
                {
                    db.Ado.ExecuteCommand(@"
CREATE TABLE #PauseCancellationProbe
(
    Id int NOT NULL,
    Value nvarchar(50) NOT NULL
);");

                    cancellation.Cancel();
                    db.Ado.CancellationToken = cancellation.Token;

                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        db.Insertable(new PauseCancellationProbe
                            {
                                Id = 1,
                                Value = "must-not-be-inserted"
                            })
                            .AS("#PauseCancellationProbe")
                            .ExecuteCommandAsync());

                    db.Ado.CancellationToken = null;
                    int rowCount = db.Ado.GetInt("SELECT COUNT(*) FROM #PauseCancellationProbe");
                    Assert.Equal(0, rowCount);
                }
                finally
                {
                    db.Ado.CancellationToken = null;
                    db.Ado.RollbackTran();
                }
            }
        }
    }
}
