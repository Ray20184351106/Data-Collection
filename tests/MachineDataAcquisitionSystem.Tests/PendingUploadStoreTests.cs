using System;
using System.Data.SQLite;
using System.IO;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class PendingUploadStoreTests
    {
        [Fact]
        public void Pending_upload_survives_store_recreation_and_records_retry_state()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "pending-upload-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                SQLiteConnection.CreateFile(databasePath);
                string connectionString = "Data Source=" + databasePath + ";Version=3;";
                Guid id;
                using (var store = new PendingUploadStore(connectionString))
                {
                    id = store.Enqueue(2, @"C:\Incoming\sample.xlsx", 17);
                    store.RecordTransientFailure(
                        id,
                        2,
                        new DateTime(2026, 8, 28, 10, 0, 15, DateTimeKind.Local),
                        "数据库连接超时");
                }

                using (var reopened = new PendingUploadStore(connectionString))
                {
                    PendingUploadRecord record = reopened.Get(id);
                    Assert.NotNull(record);
                    Assert.Equal(2, record.MachineId);
                    Assert.Equal(17, record.ModelId);
                    Assert.Equal(2, record.AttemptCount);
                    Assert.Equal(PendingUploadStatus.WaitingRetry, record.Status);
                    Assert.Equal("数据库连接超时", record.LastError);
                }
            }
            finally
            {
                if (File.Exists(databasePath))
                    File.Delete(databasePath);
            }
        }

        [Fact]
        public void Completing_an_upload_removes_it_from_the_pending_store()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "pending-upload-complete-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                SQLiteConnection.CreateFile(databasePath);
                using (var store = new PendingUploadStore("Data Source=" + databasePath + ";Version=3;"))
                {
                    Guid id = store.Enqueue(1, @"C:\Incoming\done.xlsx", 9);
                    store.Complete(id);
                    Assert.Null(store.Get(id));
                }
            }
            finally
            {
                if (File.Exists(databasePath))
                    File.Delete(databasePath);
            }
        }

        [Fact]
        public void Re_enqueuing_the_same_source_file_resets_stale_retry_state()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                "pending-upload-reset-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                SQLiteConnection.CreateFile(databasePath);
                using (var store = new PendingUploadStore("Data Source=" + databasePath + ";Version=3;"))
                {
                    Guid originalId = store.Enqueue(3, @"C:\Incoming\retry.xlsx", 21);
                    store.RecordTransientFailure(
                        originalId,
                        4,
                        new DateTime(2026, 8, 28, 10, 5, 0, DateTimeKind.Local),
                        "旧的网络错误");

                    Guid requeuedId = store.Enqueue(3, @"C:\Incoming\retry.xlsx", 22);
                    PendingUploadRecord record = store.Get(requeuedId);

                    Assert.Equal(originalId, requeuedId);
                    Assert.Equal(22, record.ModelId);
                    Assert.Equal(PendingUploadStatus.Pending, record.Status);
                    Assert.Equal(0, record.AttemptCount);
                    Assert.Null(record.NextAttemptAt);
                    Assert.Null(record.LastError);
                }
            }
            finally
            {
                if (File.Exists(databasePath))
                    File.Delete(databasePath);
            }
        }
    }
}
