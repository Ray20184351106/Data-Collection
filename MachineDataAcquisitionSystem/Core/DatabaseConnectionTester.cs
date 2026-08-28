using System;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

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

        public static Task<DatabaseConnectionTestResult> TryOpenAsync(
            string databaseType,
            string connectionString,
            CancellationToken cancellationToken)
        {
            return TryOpenAsync(
                databaseType,
                connectionString,
                DefaultTimeout,
                cancellationToken,
                OpenConnectionAsync);
        }

        internal static async Task<DatabaseConnectionTestResult> TryOpenAsync(
            string databaseType,
            string connectionString,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            Func<string, string, CancellationToken, Task> connectionProbe)
        {
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            if (connectionProbe == null)
                throw new ArgumentNullException(nameof(connectionProbe));
            if (!TryValidateConnectionString(databaseType, connectionString, out string validationError))
                return DatabaseConnectionTestResult.Failed(validationError);

            cancellationToken.ThrowIfCancellationRequested();

            string effectiveConnectionString;
            try
            {
                effectiveConnectionString = PrepareConnectionString(databaseType, connectionString, timeout);
            }
            catch (Exception ex)
            {
                return DatabaseConnectionTestResult.Failed("数据库连接配置无效：" + ex.Message);
            }

            using (var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCancellation.CancelAfter(timeout);
                Task probeTask;
                try
                {
                    probeTask = connectionProbe(
                        databaseType,
                        effectiveConnectionString,
                        timeoutCancellation.Token);
                }
                catch (Exception ex)
                {
                    return DatabaseConnectionTestResult.Failed("数据库连接失败：" + ex.Message);
                }

                Task timeoutTask = Task.Delay(timeout, cancellationToken);
                Task completed = await Task.WhenAny(probeTask, timeoutTask).ConfigureAwait(false);
                if (completed == timeoutTask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    timeoutCancellation.Cancel();
                    ObserveFault(probeTask);
                    return DatabaseConnectionTestResult.Timeout(timeout);
                }

                try
                {
                    await probeTask.ConfigureAwait(false);
                    return DatabaseConnectionTestResult.Success();
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return DatabaseConnectionTestResult.Timeout(timeout);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return DatabaseConnectionTestResult.Failed("数据库连接失败：" + ex.Message);
                }
            }
        }

        private static string PrepareConnectionString(
            string databaseType,
            string connectionString,
            TimeSpan timeout)
        {
            int timeoutSeconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
            if (string.Equals(databaseType, "SQL Server", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(databaseType, "SQLServer", StringComparison.OrdinalIgnoreCase))
                return PrepareSqlServerConnectionString(connectionString, timeoutSeconds);

            if (string.Equals(databaseType, "MySQL", StringComparison.OrdinalIgnoreCase))
                return PrepareMySqlConnectionString(connectionString, timeoutSeconds);

            if (string.Equals(databaseType, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
                return PreparePostgreSqlConnectionString(connectionString, timeoutSeconds);

            if (string.Equals(databaseType, "SQLite", StringComparison.OrdinalIgnoreCase))
                return PrepareSqliteConnectionString(connectionString, timeout);

            throw new NotSupportedException("不支持的数据库类型：" + (databaseType ?? "<空>"));
        }

        private static string PrepareSqlServerConnectionString(string connectionString, int timeoutSeconds)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            builder.ConnectTimeout = CapTimeout(builder.ConnectTimeout, timeoutSeconds);
            return builder.ConnectionString;
        }

        private static string PrepareMySqlConnectionString(string connectionString, int timeoutSeconds)
        {
            var builder = new MySqlConnectionStringBuilder(connectionString);
            builder.ConnectionTimeout = (uint)CapTimeout((int)builder.ConnectionTimeout, timeoutSeconds);
            return builder.ConnectionString;
        }

        private static string PreparePostgreSqlConnectionString(string connectionString, int timeoutSeconds)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            builder.Timeout = CapTimeout(builder.Timeout, timeoutSeconds);
            return builder.ConnectionString;
        }

        private static int CapTimeout(int configuredSeconds, int timeoutSeconds)
        {
            return configuredSeconds <= 0
                ? timeoutSeconds
                : Math.Min(configuredSeconds, timeoutSeconds);
        }

        private static string PrepareSqliteConnectionString(string connectionString, TimeSpan timeout)
        {
            var builder = connectionString.IndexOf('=') >= 0
                ? new SQLiteConnectionStringBuilder(connectionString)
                : new SQLiteConnectionStringBuilder { DataSource = connectionString, Version = 3 };
            string dataSource = builder.DataSource;
            if (string.IsNullOrWhiteSpace(dataSource) ||
                string.Equals(dataSource.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SQLite 的服务器地址必须填写现有数据库文件路径，不能使用 localhost。");

            string databasePath = Path.GetFullPath(dataSource);
            if (!File.Exists(databasePath))
                throw new FileNotFoundException("SQLite 数据库文件不存在。", databasePath);
            builder.DataSource = databasePath;
            builder.FailIfMissing = true;
            builder.ReadOnly = true;
            builder.BusyTimeout = (int)Math.Min(int.MaxValue, Math.Ceiling(timeout.TotalMilliseconds));
            return builder.ConnectionString;
        }

        private static async Task OpenConnectionAsync(
            string databaseType,
            string connectionString,
            CancellationToken cancellationToken)
        {
            if (string.Equals(databaseType, "SQLite", StringComparison.OrdinalIgnoreCase))
                await OpenSqliteAsync(connectionString, cancellationToken).ConfigureAwait(false);
            else if (string.Equals(databaseType, "SQL Server", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(databaseType, "SQLServer", StringComparison.OrdinalIgnoreCase))
                await OpenSqlServerAsync(connectionString, cancellationToken).ConfigureAwait(false);
            else if (string.Equals(databaseType, "MySQL", StringComparison.OrdinalIgnoreCase))
                await OpenMySqlAsync(connectionString, cancellationToken).ConfigureAwait(false);
            else if (string.Equals(databaseType, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
                await OpenPostgreSqlAsync(connectionString, cancellationToken).ConfigureAwait(false);
            else
                throw new NotSupportedException("不支持的数据库类型：" + (databaseType ?? "<空>"));
        }

        private static Task OpenSqliteAsync(string connectionString, CancellationToken cancellationToken)
        {
            return OpenAndProbeAsync(
                new SQLiteConnection(connectionString),
                "PRAGMA schema_version;",
                cancellationToken);
        }

        private static Task OpenSqlServerAsync(string connectionString, CancellationToken cancellationToken)
        {
            return OpenAndProbeAsync(new SqlConnection(connectionString), "SELECT 1;", cancellationToken);
        }

        private static Task OpenMySqlAsync(string connectionString, CancellationToken cancellationToken)
        {
            return OpenAndProbeAsync(new MySqlConnection(connectionString), "SELECT 1;", cancellationToken);
        }

        private static Task OpenPostgreSqlAsync(string connectionString, CancellationToken cancellationToken)
        {
            return OpenAndProbeAsync(new NpgsqlConnection(connectionString), "SELECT 1;", cancellationToken);
        }

        private static async Task OpenAndProbeAsync(
            DbConnection connection,
            string probeSql,
            CancellationToken cancellationToken)
        {
            using (connection)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = probeSql;
                    command.CommandTimeout = 5;
                    object result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    if (result == null || result == DBNull.Value)
                        throw new InvalidOperationException("数据库健康检查没有返回结果。");
                }
            }
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(
                completed => { var ignored = completed.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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

    public sealed class DatabaseConnectionTestResult
    {
        private DatabaseConnectionTestResult(bool isSuccess, bool timedOut, string error)
        {
            IsSuccess = isSuccess;
            TimedOut = timedOut;
            Error = error;
        }

        public bool IsSuccess { get; private set; }
        public bool TimedOut { get; private set; }
        public string Error { get; private set; }

        internal static DatabaseConnectionTestResult Success()
        {
            return new DatabaseConnectionTestResult(true, false, null);
        }

        internal static DatabaseConnectionTestResult Failed(string error)
        {
            return new DatabaseConnectionTestResult(false, false, error);
        }

        internal static DatabaseConnectionTestResult Timeout(TimeSpan timeout)
        {
            return new DatabaseConnectionTestResult(
                false,
                true,
                "数据库连接测试超时（" + Math.Ceiling(timeout.TotalSeconds) + " 秒）。");
        }
    }
}
