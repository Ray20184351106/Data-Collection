using MachineDataAcquisitionSystem.Core;
using System;
using System.Data.SQLite;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class DatabaseConnectionTesterTests
    {
        [Fact]
        public void Unknown_database_type_never_reports_a_successful_connection()
        {
            bool connected = DatabaseConnectionTester.TryOpen(
                "Unknown",
                "anything",
                out string error);

            Assert.False(connected);
            Assert.Contains("不支持", error);
        }

        [Fact]
        public void SQLite_localhost_placeholder_never_reports_a_successful_connection()
        {
            bool connected = DatabaseConnectionTester.TryOpen(
                "SQLite",
                "Data Source=localhost;Version=3;",
                out string error);

            Assert.False(connected);
            Assert.Contains("不能使用 localhost", error);
        }

        [Fact]
        public void SQLite_connection_test_opens_an_existing_database_without_modifying_the_configuration()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "acquisition-connection-test-" + Guid.NewGuid().ToString("N") + ".db");

            try
            {
                SQLiteConnection.CreateFile(databasePath);
                using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "CREATE TABLE HealthCheck (Id INTEGER PRIMARY KEY);";
                        command.ExecuteNonQuery();
                    }
                }

                bool connected = DatabaseConnectionTester.TryOpen(
                    "SQLite",
                    "Data Source=" + databasePath + ";Version=3;",
                    out string error);

                Assert.True(connected, error);
                Assert.Null(error);
            }
            finally
            {
                if (File.Exists(databasePath))
                    File.Delete(databasePath);
            }
        }

        [Fact]
        public void SQLite_connection_test_rejects_a_missing_database_without_creating_it()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "acquisition-missing-" + Guid.NewGuid().ToString("N") + ".db");

            bool connected = DatabaseConnectionTester.TryOpen(
                "SQLite",
                "Data Source=" + databasePath + ";Version=3;",
                out string error);

            Assert.False(connected);
            Assert.Contains("不存在", error);
            Assert.False(File.Exists(databasePath));
        }

        [Fact]
        public void PostgreSql_connection_test_returns_false_when_a_real_tcp_connection_is_refused()
        {
            bool connected = DatabaseConnectionTester.TryOpen(
                "PostgreSQL",
                "Host=127.0.0.1;Port=1;Database=postgres;Username=invalid;Password=invalid;Timeout=1;Command Timeout=1;",
                out string error);

            Assert.False(connected);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void SqlServer_connection_test_rejects_a_connection_string_without_a_target_database()
        {
            bool connected = DatabaseConnectionTester.TryOpen(
                "SQL Server",
                "Server=127.0.0.1,1;Integrated Security=True;Connect Timeout=1;",
                out string error);

            Assert.False(connected);
            Assert.Contains("数据库名", error);
        }

        [Fact]
        public async Task Async_connection_test_times_out_even_when_the_provider_probe_does_not_complete()
        {
            var neverCompletes = new TaskCompletionSource<bool>();
            var stopwatch = Stopwatch.StartNew();

            DatabaseConnectionTestResult result = await DatabaseConnectionTester.TryOpenAsync(
                "SQL Server",
                "Server=localhost;Database=FileAcquisitionDemo;Integrated Security=True;",
                TimeSpan.FromMilliseconds(50),
                CancellationToken.None,
                (databaseType, connectionString, cancellationToken) => neverCompletes.Task);

            stopwatch.Stop();
            Assert.False(result.IsSuccess);
            Assert.True(result.TimedOut);
            Assert.Contains("超时", result.Error);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Async_connection_test_honors_user_cancellation()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    DatabaseConnectionTester.TryOpenAsync(
                        "SQL Server",
                        "Server=localhost;Database=FileAcquisitionDemo;Integrated Security=True;",
                        TimeSpan.FromSeconds(5),
                        cancellation.Token,
                        (databaseType, connectionString, cancellationToken) => Task.Delay(5000, cancellationToken)));
            }
        }

        [Fact]
        public async Task Async_connection_test_caps_the_provider_connection_timeout_without_changing_the_saved_value()
        {
            const string savedConnectionString =
                "Server=localhost;Database=FileAcquisitionDemo;Integrated Security=True;Connect Timeout=30;";
            string effectiveConnectionString = null;

            DatabaseConnectionTestResult result = await DatabaseConnectionTester.TryOpenAsync(
                "SQL Server",
                savedConnectionString,
                TimeSpan.FromSeconds(5),
                CancellationToken.None,
                (databaseType, connectionString, cancellationToken) =>
                {
                    effectiveConnectionString = connectionString;
                    return Task.CompletedTask;
                });

            Assert.True(result.IsSuccess, result.Error);
            Assert.Contains("Connect Timeout=5", effectiveConnectionString);
            Assert.Contains("Connect Timeout=30", savedConnectionString);
        }

        [Fact]
        public async Task Async_connection_test_replaces_an_infinite_provider_timeout_with_the_test_limit()
        {
            string effectiveConnectionString = null;

            DatabaseConnectionTestResult result = await DatabaseConnectionTester.TryOpenAsync(
                "SQL Server",
                "Server=localhost;Database=FileAcquisitionDemo;Integrated Security=True;Connect Timeout=0;",
                TimeSpan.FromSeconds(5),
                CancellationToken.None,
                (databaseType, connectionString, cancellationToken) =>
                {
                    effectiveConnectionString = connectionString;
                    return Task.CompletedTask;
                });

            Assert.True(result.IsSuccess, result.Error);
            Assert.Contains("Connect Timeout=5", effectiveConnectionString);
        }
    }
}
