using System;
using System.Data.SqlClient;
using System.IO;
using System.Net.Sockets;

namespace MachineDataAcquisitionSystem.Core
{
    public static class DatabaseRetryPolicy
    {
        private static readonly int[] BackoffSeconds = { 5, 15, 30, 60 };
        private static readonly int[] TransientSqlNumbers =
        {
            -2, 20, 53, 64, 233, 1205, 4060, 10928, 10929,
            40197, 40501, 40613, 10053, 10054, 10060
        };

        public static TimeSpan GetDelay(int attempt)
        {
            if (attempt <= 0) throw new ArgumentOutOfRangeException(nameof(attempt));
            int index = Math.Min(attempt - 1, BackoffSeconds.Length - 1);
            return TimeSpan.FromSeconds(BackoffSeconds[index]);
        }

        public static bool IsTransient(Exception exception)
        {
            if (exception == null) return false;
            if (exception is TimeoutException || exception is IOException || exception is SocketException)
                return true;
            if (exception is SqlException sqlException)
            {
                foreach (SqlError error in sqlException.Errors)
                {
                    if (Array.IndexOf(TransientSqlNumbers, error.Number) >= 0)
                        return true;
                }
                return false;
            }

            string message = exception.Message ?? string.Empty;
            if (ContainsPermanentDatabaseMessage(message)) return false;
            if (ContainsTransientDatabaseMessage(message)) return true;
            return exception.InnerException != null && IsTransient(exception.InnerException);
        }

        private static bool ContainsPermanentDatabaseMessage(string message)
        {
            return Contains(message, "列名") && Contains(message, "无效") ||
                   Contains(message, "对象名") && Contains(message, "无效") ||
                   Contains(message, "不能将值 NULL 插入列") ||
                   Contains(message, "PRIMARY KEY") ||
                   Contains(message, "UNIQUE constraint") ||
                   Contains(message, "duplicate key");
        }

        private static bool ContainsTransientDatabaseMessage(string message)
        {
            return Contains(message, "超时") ||
                   Contains(message, "timeout") ||
                   Contains(message, "network-related") ||
                   Contains(message, "transport-level") ||
                   Contains(message, "连接已关闭") ||
                   Contains(message, "连接被强制关闭") ||
                   Contains(message, "connection refused") ||
                   Contains(message, "server was not found");
        }

        private static bool Contains(string value, string expected)
        {
            return value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
