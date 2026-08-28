using System.Threading;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class DatabaseConnectionTesterSqlServerIntegrationTests
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task Async_connection_test_reaches_the_real_local_sql_server()
        {
            DatabaseConnectionTestResult result = await DatabaseConnectionTester.TryOpenAsync(
                @"SQL Server",
                @"Server=.\SQLEXPRESS;Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5;",
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error);
        }
    }
}
