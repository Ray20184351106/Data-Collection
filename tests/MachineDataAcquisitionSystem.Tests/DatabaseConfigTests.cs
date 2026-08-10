using MachineDataAcquisitionSystem.Models;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class DatabaseConfigTests
    {
        [Fact]
        public void SqlServer_without_user_credentials_uses_windows_authentication()
        {
            var config = new DatabaseConfig
            {
                DbType = "SQL Server",
                Server = @"localhost\SQLEXPRESS",
                DatabaseName = "FileAcquisitionDemo",
                UserId = "",
                Password = ""
            };

            string connectionString = config.GetConnectionString();

            Assert.Contains("Integrated Security=True", connectionString);
            Assert.DoesNotContain("User Id=", connectionString);
            Assert.DoesNotContain("Password=", connectionString);
        }

        [Fact]
        public void SqlServer_with_user_credentials_uses_sql_authentication()
        {
            var config = new DatabaseConfig
            {
                DbType = "SQL Server",
                Server = @"localhost\SQLEXPRESS",
                DatabaseName = "FileAcquisitionDemo",
                UserId = "collector",
                Password = "secret"
            };

            string connectionString = config.GetConnectionString();

            Assert.Contains("User Id=collector", connectionString);
            Assert.Contains("Password=secret", connectionString);
            Assert.DoesNotContain("Integrated Security=True", connectionString);
        }
    }
}
