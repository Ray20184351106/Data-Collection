using System.Text.Json;
using Acquisition.Contracts;
using Microsoft.Data.Sqlite;

namespace Acquisition.Agent.Storage;

public sealed class AgentLocalStore(string databasePath) : IAsyncDisposable
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS OutboxRecords (
                RecordId TEXT PRIMARY KEY, PayloadJson TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ExecutedCommands (
                CommandId TEXT PRIMARY KEY, State INTEGER NOT NULL, Message TEXT NULL, UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS LegacyCommands (
                CommandId TEXT PRIMARY KEY, CommandType INTEGER NOT NULL, DeviceId TEXT NULL, ParametersJson TEXT NOT NULL,
                State INTEGER NOT NULL, Message TEXT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS LocalDeviceStatuses (
                DeviceId TEXT PRIMARY KEY, Name TEXT NOT NULL, State INTEGER NOT NULL, QueueDepth INTEGER NOT NULL,
                TodaySuccess INTEGER NOT NULL, TodayFailure INTEGER NOT NULL, LastProcessedAtUtc TEXT NULL,
                LastError TEXT NULL, UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ConfigState (
                Id INTEGER PRIMARY KEY CHECK (Id = 1), CurrentVersion INTEGER NULL, CurrentJson TEXT NULL,
                PreviousVersion INTEGER NULL, PreviousJson TEXT NULL, UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS AgentMeta (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task EnqueueRecordAsync(CollectionRecordSummary record, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO OutboxRecords (RecordId, PayloadJson, CreatedAtUtc) VALUES ($id, $json, $at)";
        command.Parameters.AddWithValue("$id", record.RecordId.ToString("N"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(record));
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<CollectionRecordSummary>> GetPendingRecordsAsync(int limit, CancellationToken cancellationToken)
    {
        var records = new List<CollectionRecordSummary>();
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM OutboxRecords ORDER BY CreatedAtUtc LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var record = JsonSerializer.Deserialize<CollectionRecordSummary>(reader.GetString(0));
            if (record is not null) records.Add(record);
        }
        return records;
    }

    public async Task MarkRecordsSentAsync(IEnumerable<Guid> recordIds, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var id in recordIds)
        {
            var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "DELETE FROM OutboxRecords WHERE RecordId = $id";
            command.Parameters.AddWithValue("$id", id.ToString("N"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CountPendingRecordsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM OutboxRecords";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> TryClaimCommandAsync(Guid commandId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO ExecutedCommands (CommandId, State, UpdatedAtUtc) VALUES ($id, $state, $at)";
        command.Parameters.AddWithValue("$id", commandId.ToString("N"));
        command.Parameters.AddWithValue("$state", (int)CommandExecutionState.Executing);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1) return true;
        var reclaim = connection.CreateCommand();
        reclaim.CommandText = "UPDATE ExecutedCommands SET UpdatedAtUtc=$at WHERE CommandId=$id AND State=$state AND UpdatedAtUtc < $cutoff";
        reclaim.Parameters.AddWithValue("$id", commandId.ToString("N"));
        reclaim.Parameters.AddWithValue("$state", (int)CommandExecutionState.Executing);
        reclaim.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        reclaim.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
        return await reclaim.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<(CommandExecutionState State, string? Message)?> GetCommandResultAsync(Guid commandId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT State,Message FROM ExecutedCommands WHERE CommandId=$id";
        command.Parameters.AddWithValue("$id", commandId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ((CommandExecutionState)reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    public async Task CompleteCommandAsync(Guid commandId, CommandExecutionState state, string? message, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE ExecutedCommands SET State=$state, Message=$message, UpdatedAtUtc=$at WHERE CommandId=$id";
        command.Parameters.AddWithValue("$id", commandId.ToString("N"));
        command.Parameters.AddWithValue("$state", (int)state);
        command.Parameters.AddWithValue("$message", (object?)message ?? DBNull.Value);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task EnqueueLegacyCommandAsync(CommandEnvelope commandEnvelope, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO LegacyCommands
            (CommandId, CommandType, DeviceId, ParametersJson, State, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $type, $device, $json, 0, $at, $at)
            """;
        command.Parameters.AddWithValue("$id", commandEnvelope.CommandId.ToString("N"));
        command.Parameters.AddWithValue("$type", (int)commandEnvelope.Type);
        command.Parameters.AddWithValue("$device", (object?)commandEnvelope.DeviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$json", commandEnvelope.ParametersJson);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(bool Completed, bool Succeeded, string? Message)> GetLegacyCommandResultAsync(Guid commandId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT State, Message FROM LegacyCommands WHERE CommandId=$id";
        command.Parameters.AddWithValue("$id", commandId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (false, false, null);
        var state = reader.GetInt32(0);
        return (state is 2 or 3, state == 2, reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    public async Task<List<DeviceRuntimeStatus>> GetDeviceStatusesAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<DeviceRuntimeStatus>();
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT DeviceId,Name,State,QueueDepth,TodaySuccess,TodayFailure,LastProcessedAtUtc,LastError FROM LocalDeviceStatuses ORDER BY Name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) statuses.Add(new DeviceRuntimeStatus
        {
            DeviceId = reader.GetString(0), Name = reader.GetString(1), State = (RuntimeState)reader.GetInt32(2),
            QueueDepth = reader.GetInt32(3), TodaySuccess = reader.GetInt32(4), TodayFailure = reader.GetInt32(5),
            LastProcessedAtUtc = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            LastError = reader.IsDBNull(7) ? null : reader.GetString(7)
        });
        return statuses;
    }

    public async Task SaveAppliedConfigAsync(ConfigPackage package, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ConfigState (Id,CurrentVersion,CurrentJson,PreviousVersion,PreviousJson,UpdatedAtUtc)
            VALUES (1,$version,$json,NULL,NULL,$at)
            ON CONFLICT(Id) DO UPDATE SET
              PreviousVersion=CurrentVersion, PreviousJson=CurrentJson,
              CurrentVersion=$version, CurrentJson=$json, UpdatedAtUtc=$at
            """;
        command.Parameters.AddWithValue("$version", package.Version);
        command.Parameters.AddWithValue("$json", package.PayloadJson);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> GetLongMetaAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT Value FROM AgentMeta WHERE Key=$key";
        command.Parameters.AddWithValue("$key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null && long.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    public async Task SetLongMetaAsync(string key, long value, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AgentMeta (Key,Value) VALUES ($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=$value";
        command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$value", value.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
