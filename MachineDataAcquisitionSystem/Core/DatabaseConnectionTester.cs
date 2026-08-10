using System;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.IO;
using MySql.Data.MySqlClient;
using Npgsql;

namespace MachineDataAcquisitionSystem.Core
{
    /// <summary>
    /// Performs the real database connection check used by the configuration screen.
    /// SQLite is opened read-only so a mistyped path cannot create a new empty file
    /// and be reported as a successful connection.
    /// </summary>
    public static class DatabaseConnectionTester
    {
        public static bool TryOpen(string databaseType, string connectionString, out string error)
        {
            if (!TryValidateConnectionString(databaseType, connectionString, out error))
                return false;

            try
            {
                if (string.Equals(databaseType, "SQLite", StringComparison.OrdinalIgnoreCase))
                {
                    return TryOpenSqlite(connectionString, out error);
                }

                if (string.Equals(databaseType, "SQL Server", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(databaseType, "SQLServer", StringComparison.OrdinalIgnoreCase))
                {
                    return TryOpenSqlServer(connectionString);
                }

                if (string.Equals(databaseType, "MySQL", StringComparison.OrdinalIgnoreCase))
                {
                    return TryOpenMySql(connectionString);
                }

                if (string.Equals(databaseType, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
                {
                    return TryOpenPostgreSql(connectionString);
                }

                error = "不支持的数据库类型：" + (databaseType ?? "<空>");
                return false;
            }
            catch (Exception ex)
            {
                // The connection string may contain a password, so never echo it.
                error = "数据库连接失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Validates fields which a successful server login alone cannot prove.
        /// In particular, SQL Server falls back to a default database when the
        /// connection string omits Database/Initial Catalog.
        /// </summary>
        public static bool TryValidateConnectionString(string databaseType, string connectionString, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                error = "连接字符串为空。";
                return false;
            }

            if (string.Equals(databaseType, "SQL Server", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(databaseType, "SQLServer", StringComparison.OrdinalIgnoreCase))
            {
                SqlConnectionStringBuilder builder;
                try
                {
                    builder = new SqlConnectionStringBuilder(connectionString);
                }
                catch (Exception ex)
                {
                    error = "SQL Server 连接字符串无效：" + ex.Message;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(builder.DataSource))
                {
                    error = "SQL Server 服务器地址不能为空。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
                {
                    error = "SQL Server 数据库名不能为空；请填写目标数据库，例如 FileAcquisitionDemo。";
                    return false;
                }
            }

            return true;
        }

        private static bool TryOpenSqlite(string connectionString, out string error)
        {
            error = null;
            var builder = connectionString.IndexOf('=') >= 0
                ? new SQLiteConnectionStringBuilder(connectionString)
                : new SQLiteConnectionStringBuilder { DataSource = connectionString, Version = 3 };

            string dataSource = builder.DataSource;
            if (string.IsNullOrWhiteSpace(dataSource) ||
                string.Equals(dataSource.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
            {
                error = "SQLite 的服务器地址必须填写现有数据库文件路径，不能使用 localhost。";
                return false;
            }

            string databasePath = Path.GetFullPath(dataSource);
            if (!File.Exists(databasePath))
            {
                error = "SQLite 数据库文件不存在：" + databasePath;
                return false;
            }

            builder.DataSource = databasePath;
            builder.FailIfMissing = true;
            builder.ReadOnly = true;
            using (var connection = new SQLiteConnection(builder.ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA schema_version;";
                    object result = command.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                    {
                        error = "SQLite 数据库无法读取。";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryOpenSqlServer(string connectionString)
        {
            using (var connection = new SqlConnection(connectionString))
                connection.Open();
            return true;
        }

        private static bool TryOpenMySql(string connectionString)
        {
            using (var connection = new MySqlConnection(connectionString))
                connection.Open();
            return true;
        }

        private static bool TryOpenPostgreSql(string connectionString)
        {
            using (var connection = new NpgsqlConnection(connectionString))
                connection.Open();
            return true;
        }
    }
}
