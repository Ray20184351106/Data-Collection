using MachineDataAcquisitionSystem.Core;
using System;
using System.Data.SQLite;
using System.IO;
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
    }
}
