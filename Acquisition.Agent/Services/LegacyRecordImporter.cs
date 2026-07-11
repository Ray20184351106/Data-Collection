using Acquisition.Agent.Storage;
using Acquisition.Contracts;
using Microsoft.Data.Sqlite;

namespace Acquisition.Agent.Services;

public sealed class LegacyRecordImporter(AgentOptions options, AgentLocalStore store)
{
    public async Task<int> ImportAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.LegacyDatabasePath) || !File.Exists(options.LegacyDatabasePath)) return 0;
        var cursor = await store.GetLongMetaAsync("legacy-record-cursor", cancellationToken);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(options.LegacyDatabasePath), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, MachineId, FileName, Status, RecordCount, ErrorMsg, ProcessTime, Duration
            FROM FileProcessRecord WHERE Id > $cursor ORDER BY Id LIMIT 500
            """;
        command.Parameters.AddWithValue("$cursor", cursor);
        var imported = 0; var highest = cursor;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            highest = reader.GetInt64(0);
            DateTimeOffset processedAt = DateTimeOffset.UtcNow;
            if (!reader.IsDBNull(6) && DateTime.TryParse(reader.GetValue(6).ToString(), out var parsed)) processedAt = new DateTimeOffset(parsed);
            await store.EnqueueRecordAsync(new CollectionRecordSummary
            {
                RecordId = CreateStableRecordId(options.AgentId, highest), AgentId = options.AgentId,
                DeviceId = reader.GetInt32(1).ToString(), FileName = reader.GetString(2),
                Succeeded = string.Equals(reader.GetString(3), "成功", StringComparison.Ordinal),
                RecordCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5), ProcessedAtUtc = processedAt,
                DurationMilliseconds = reader.IsDBNull(7) ? 0 : reader.GetInt64(7)
            }, cancellationToken);
            imported++;
        }
        if (highest > cursor) await store.SetLongMetaAsync("legacy-record-cursor", highest, cancellationToken);
        return imported;
    }

    private static Guid CreateStableRecordId(string agentId, long legacyId)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{agentId}:{legacyId}"));
        return new Guid(bytes);
    }
}
