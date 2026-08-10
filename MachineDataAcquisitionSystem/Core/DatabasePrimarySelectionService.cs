using MachineDataAcquisitionSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MachineDataAcquisitionSystem.Core
{
    public static class DatabaseConfigurationRules
    {
        private static readonly HashSet<string> ConnectionProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "DbType",
            "Server",
            "Port",
            "DatabaseName",
            "UserId",
            "Password"
        };

        public static bool ConnectionStringShouldBeRegenerated(string propertyName)
        {
            return !string.IsNullOrWhiteSpace(propertyName) && ConnectionProperties.Contains(propertyName);
        }
    }

    public static class DatabasePrimarySelectionService
    {
        public static void SetPrimary(IList<DatabaseConfig> databases, DatabaseConfig selectedDatabase)
        {
            if (databases == null) throw new ArgumentNullException(nameof(databases));
            if (selectedDatabase == null) throw new ArgumentNullException(nameof(selectedDatabase));

            DatabaseConfig configuredDatabase = databases.FirstOrDefault(database =>
                ReferenceEquals(database, selectedDatabase) ||
                (!string.IsNullOrWhiteSpace(database.Id) &&
                 string.Equals(database.Id, selectedDatabase.Id, StringComparison.Ordinal)));

            if (configuredDatabase == null)
                throw new InvalidOperationException("所选数据库不在当前配置中。请刷新后重试。");

            foreach (DatabaseConfig database in databases)
                database.IsPrimary = ReferenceEquals(database, configuredDatabase);
        }
    }
}
