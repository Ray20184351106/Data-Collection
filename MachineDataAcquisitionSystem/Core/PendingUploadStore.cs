using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace MachineDataAcquisitionSystem.Core
{
    public enum PendingUploadStatus
    {
        Pending = 0,
        WaitingRetry = 1,
        PermanentFailure = 2
    }

    public sealed class PendingUploadRecord
    {
        public Guid Id { get; set; }
        public int MachineId { get; set; }
        public string FilePath { get; set; }
        public int ModelId { get; set; }
        public PendingUploadStatus Status { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public string LastError { get; set; }
    }

    public sealed class PendingUploadStore : IDisposable
    {
        private const string DateFormat = "O";
        private readonly string _connectionString;
        private readonly object _sync = new object();

        public PendingUploadStore(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("本地队列数据库连接字符串不能为空。", nameof(connectionString));
            var builder = new SQLiteConnectionStringBuilder(connectionString)
            {
                Pooling = false,
                BusyTimeout = 5000
            };
            _connectionString = builder.ConnectionString;
            EnsureSchema();
        }

        public Guid Enqueue(int machineId, string filePath, int modelId)
        {
            if (machineId <= 0) throw new ArgumentOutOfRangeException(nameof(machineId));
            if (modelId <= 0) throw new ArgumentOutOfRangeException(nameof(modelId));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("待入库文件路径不能为空。", nameof(filePath));

            string normalizedPath = Path.GetFullPath(filePath);
            lock (_sync)
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                Guid? existing = FindId(connection, transaction, machineId, normalizedPath);
                if (existing.HasValue)
                {
                    using (var reset = new SQLiteCommand(@"
UPDATE PendingUploads
SET ModelId=@ModelId,
    Status=@Status,
    AttemptCount=0,
    NextAttemptAt=NULL,
    LastError=NULL,
    UpdatedAt=@UpdatedAt
WHERE Id=@Id;", connection, transaction))
                    {
                        reset.Parameters.AddWithValue("@ModelId", modelId);
                        reset.Parameters.AddWithValue("@Status", (int)PendingUploadStatus.Pending);
                        reset.Parameters.AddWithValue("@UpdatedAt", Format(DateTime.Now));
                        reset.Parameters.AddWithValue("@Id", existing.Value.ToString("N"));
                        reset.ExecuteNonQuery();
                    }
                    transaction.Commit();
                    return existing.Value;
                }

                Guid id = Guid.NewGuid();
                using (var insert = new SQLiteCommand(@"
INSERT INTO PendingUploads
    (Id, MachineId, FilePath, ModelId, Status, AttemptCount, NextAttemptAt, LastError, CreatedAt, UpdatedAt)
VALUES
    (@Id, @MachineId, @FilePath, @ModelId, @Status, 0, NULL, NULL, @CreatedAt, @UpdatedAt);",
                    connection,
                    transaction))
                {
                    string now = Format(DateTime.Now);
                    insert.Parameters.AddWithValue("@Id", id.ToString("N"));
                    insert.Parameters.AddWithValue("@MachineId", machineId);
                    insert.Parameters.AddWithValue("@FilePath", normalizedPath);
                    insert.Parameters.AddWithValue("@ModelId", modelId);
                    insert.Parameters.AddWithValue("@Status", (int)PendingUploadStatus.Pending);
                    insert.Parameters.AddWithValue("@CreatedAt", now);
                    insert.Parameters.AddWithValue("@UpdatedAt", now);
                    insert.ExecuteNonQuery();
                }
                transaction.Commit();
                return id;
            }
        }

        public void RecordTransientFailure(
            Guid id,
            int attemptCount,
            DateTime nextAttemptAt,
            string error)
        {
            UpdateFailure(id, PendingUploadStatus.WaitingRetry, attemptCount, nextAttemptAt, error);
        }

        public void RecordPermanentFailure(Guid id, int attemptCount, string error)
        {
            UpdateFailure(id, PendingUploadStatus.PermanentFailure, attemptCount, null, error);
        }

        public PendingUploadRecord Get(Guid id)
        {
            lock (_sync)
            using (var connection = Open())
            using (var command = new SQLiteCommand(@"
SELECT Id, MachineId, FilePath, ModelId, Status, AttemptCount, NextAttemptAt, LastError
FROM PendingUploads
WHERE Id=@Id;", connection))
            {
                command.Parameters.AddWithValue("@Id", id.ToString("N"));
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new PendingUploadRecord
                    {
                        Id = Guid.ParseExact(reader.GetString(0), "N"),
                        MachineId = reader.GetInt32(1),
                        FilePath = reader.GetString(2),
                        ModelId = reader.GetInt32(3),
                        Status = (PendingUploadStatus)reader.GetInt32(4),
                        AttemptCount = reader.GetInt32(5),
                        NextAttemptAt = reader.IsDBNull(6) ? (DateTime?)null : Parse(reader.GetString(6)),
                        LastError = reader.IsDBNull(7) ? null : reader.GetString(7)
                    };
                }
            }
        }

        public void Complete(Guid id)
        {
            lock (_sync)
            using (var connection = Open())
            using (var command = new SQLiteCommand("DELETE FROM PendingUploads WHERE Id=@Id;", connection))
            {
                command.Parameters.AddWithValue("@Id", id.ToString("N"));
                command.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
        }

        private void EnsureSchema()
        {
            lock (_sync)
            using (var connection = Open())
            using (var command = new SQLiteCommand(@"
CREATE TABLE IF NOT EXISTS PendingUploads (
    Id TEXT PRIMARY KEY,
    MachineId INTEGER NOT NULL,
    FilePath TEXT NOT NULL,
    ModelId INTEGER NOT NULL,
    Status INTEGER NOT NULL,
    AttemptCount INTEGER NOT NULL DEFAULT 0,
    NextAttemptAt TEXT NULL,
    LastError TEXT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_PendingUploads_Machine_File
ON PendingUploads(MachineId, FilePath);", connection))
            {
                command.ExecuteNonQuery();
            }
        }

        private void UpdateFailure(
            Guid id,
            PendingUploadStatus status,
            int attemptCount,
            DateTime? nextAttemptAt,
            string error)
        {
            if (attemptCount <= 0) throw new ArgumentOutOfRangeException(nameof(attemptCount));
            lock (_sync)
            using (var connection = Open())
            using (var command = new SQLiteCommand(@"
UPDATE PendingUploads
SET Status=@Status,
    AttemptCount=@AttemptCount,
    NextAttemptAt=@NextAttemptAt,
    LastError=@LastError,
    UpdatedAt=@UpdatedAt
WHERE Id=@Id;", connection))
            {
                command.Parameters.AddWithValue("@Status", (int)status);
                command.Parameters.AddWithValue("@AttemptCount", attemptCount);
                command.Parameters.AddWithValue(
                    "@NextAttemptAt",
                    nextAttemptAt.HasValue ? (object)Format(nextAttemptAt.Value) : DBNull.Value);
                command.Parameters.AddWithValue("@LastError", (object)error ?? DBNull.Value);
                command.Parameters.AddWithValue("@UpdatedAt", Format(DateTime.Now));
                command.Parameters.AddWithValue("@Id", id.ToString("N"));
                command.ExecuteNonQuery();
            }
        }

        private SQLiteConnection Open()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static Guid? FindId(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int machineId,
            string filePath)
        {
            using (var command = new SQLiteCommand(@"
SELECT Id FROM PendingUploads WHERE MachineId=@MachineId AND FilePath=@FilePath;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@MachineId", machineId);
                command.Parameters.AddWithValue("@FilePath", filePath);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? (Guid?)null
                    : Guid.ParseExact(Convert.ToString(value, CultureInfo.InvariantCulture), "N");
            }
        }

        private static string Format(DateTime value)
        {
            return value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        private static DateTime Parse(string value)
        {
            return DateTime.ParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
    }
}
