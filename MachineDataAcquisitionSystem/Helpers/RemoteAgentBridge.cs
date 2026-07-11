using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MachineDataAcquisitionSystem.Helpers
{
    public sealed class AgentDeviceSnapshot
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public int State { get; set; }
        public int QueueDepth { get; set; }
        public int TodaySuccess { get; set; }
        public int TodayFailure { get; set; }
        public DateTime? LastProcessedAt { get; set; }
        public string LastError { get; set; }
    }

    public sealed class RemoteAgentBridge : IDisposable
    {
        private readonly string _connectionString;
        private readonly SynchronizationContext _uiContext;
        private readonly Action<int?> _start;
        private readonly Action<int?> _stop;
        private readonly Action _reload;
        private readonly Func<IList<AgentDeviceSnapshot>> _getSnapshots;
        private readonly System.Threading.Timer _timer;
        private int _busy;

        public RemoteAgentBridge(Action<int?> start, Action<int?> stop, Action reload, Func<IList<AgentDeviceSnapshot>> getSnapshots)
        {
            string configured = Environment.GetEnvironmentVariable("ACQUISITION_AGENT_DB");
            string path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AcquisitionAgent", "agent.db")
                : configured;
            _connectionString = $"Data Source={path};Version=3;Busy Timeout=5000;";
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _start = start; _stop = stop; _reload = reload; _getSnapshots = getSnapshots;
            _timer = new System.Threading.Timer(Poll, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        }

        private void Poll(object state)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                if (!DatabaseAvailable()) return;
                ProcessCommands();
                IList<AgentDeviceSnapshot> snapshots = null;
                _uiContext.Send(_ => snapshots = _getSnapshots(), null);
                SaveSnapshots(snapshots);
            }
            catch { /* Agent离线不能影响本地采集。 */ }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private bool DatabaseAvailable()
        {
            var builder = new SQLiteConnectionStringBuilder(_connectionString);
            return File.Exists(builder.DataSource);
        }

        private void ProcessCommands()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                var pending = new List<Tuple<string, int, string>>();
                using (var command = new SQLiteCommand("SELECT CommandId,CommandType,DeviceId FROM LegacyCommands WHERE State=0 ORDER BY CreatedAtUtc LIMIT 20", connection))
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) pending.Add(Tuple.Create(reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                foreach (var item in pending)
                {
                    SetCommandState(connection, item.Item1, 1, null);
                    try
                    {
                        int? deviceId = int.TryParse(item.Item3, out int parsed) ? parsed : (int?)null;
                        _uiContext.Send(_ => Execute(item.Item2, deviceId), null);
                        SetCommandState(connection, item.Item1, 2, "执行成功。");
                    }
                    catch (Exception ex) { SetCommandState(connection, item.Item1, 3, ex.Message); }
                }
            }
        }

        private void Execute(int type, int? deviceId)
        {
            switch (type)
            {
                case 0: _start(deviceId); break;
                case 1: _stop(deviceId); break;
                case 2: break;
                case 4: _reload(); break;
                default: throw new NotSupportedException("当前WinForms版本暂不支持该远程命令。");
            }
        }

        private static void SetCommandState(SQLiteConnection connection, string id, int state, string message)
        {
            using (var command = new SQLiteCommand("UPDATE LegacyCommands SET State=@state,Message=@message,UpdatedAtUtc=@at WHERE CommandId=@id", connection))
            {
                command.Parameters.AddWithValue("@state", state); command.Parameters.AddWithValue("@message", (object)message ?? DBNull.Value);
                command.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("O")); command.Parameters.AddWithValue("@id", id);
                command.ExecuteNonQuery();
            }
        }

        private void SaveSnapshots(IList<AgentDeviceSnapshot> snapshots)
        {
            if (snapshots == null) return;
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var snapshot in snapshots)
                    using (var command = new SQLiteCommand(@"INSERT OR REPLACE INTO LocalDeviceStatuses
                        (DeviceId,Name,State,QueueDepth,TodaySuccess,TodayFailure,LastProcessedAtUtc,LastError,UpdatedAtUtc)
                        VALUES (@id,@name,@state,@queue,@success,@failure,@last,@error,@at)", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", snapshot.DeviceId); command.Parameters.AddWithValue("@name", snapshot.Name);
                        command.Parameters.AddWithValue("@state", snapshot.State); command.Parameters.AddWithValue("@queue", snapshot.QueueDepth);
                        command.Parameters.AddWithValue("@success", snapshot.TodaySuccess); command.Parameters.AddWithValue("@failure", snapshot.TodayFailure);
                        command.Parameters.AddWithValue("@last", snapshot.LastProcessedAt.HasValue ? (object)snapshot.LastProcessedAt.Value.ToUniversalTime().ToString("O") : DBNull.Value);
                        command.Parameters.AddWithValue("@error", (object)snapshot.LastError ?? DBNull.Value); command.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("O"));
                        command.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        public void Dispose() { _timer.Dispose(); }
    }
}
