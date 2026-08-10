using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Models;
using System.Collections.Generic;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class DatabasePrimarySelectionServiceTests
    {
        [Theory]
        [InlineData("Server", true)]
        [InlineData("Port", true)]
        [InlineData("DatabaseName", true)]
        [InlineData("UserId", true)]
        [InlineData("Password", true)]
        [InlineData("DbType", true)]
        [InlineData("IsPrimary", false)]
        [InlineData("IsEnabled", false)]
        [InlineData("Name", false)]
        [InlineData("Description", false)]
        public void ConnectionStringShouldBeRegenerated_only_for_connection_fields(
            string propertyName,
            bool expected)
        {
            Assert.Equal(
                expected,
                DatabaseConfigurationRules.ConnectionStringShouldBeRegenerated(propertyName));
        }

        [Fact]
        public void SetPrimary_marks_the_selected_database_and_clears_all_other_primary_flags()
        {
            var first = new DatabaseConfig { Id = "first", Name = "数据库1", IsPrimary = true };
            var second = new DatabaseConfig { Id = "second", Name = "数据库2", IsPrimary = false };
            var third = new DatabaseConfig { Id = "third", Name = "数据库3", IsPrimary = true };
            var databases = new List<DatabaseConfig> { first, second, third };

            DatabasePrimarySelectionService.SetPrimary(databases, second);

            Assert.False(first.IsPrimary);
            Assert.True(second.IsPrimary);
            Assert.False(third.IsPrimary);
        }

        [Fact]
        public void SetPrimary_rejects_a_database_that_is_not_in_the_configuration()
        {
            var configured = new DatabaseConfig { Id = "configured", Name = "已配置" };
            var missing = new DatabaseConfig { Id = "missing", Name = "未配置" };

            var exception = Assert.Throws<System.InvalidOperationException>(() =>
                DatabasePrimarySelectionService.SetPrimary(
                    new List<DatabaseConfig> { configured },
                    missing));

            Assert.Contains("不在当前配置", exception.Message);
            Assert.False(configured.IsPrimary);
        }
    }
}
