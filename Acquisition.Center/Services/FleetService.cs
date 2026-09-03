using Acquisition.Center.Data;
using Acquisition.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Acquisition.Center.Services;

public sealed class FleetService(CenterDbContext db)
{
    public async Task<bool> ReceiveHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        var requestKey = RequestIdentity.Create(heartbeat.AgentId, heartbeat.RequestId);
        if (await db.ProcessedRequests.AnyAsync(x => x.Id == requestKey, cancellationToken)) return false;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var agent = await db.Agents.SingleOrDefaultAsync(x => x.Id == heartbeat.AgentId, cancellationToken);
        if (agent is null)
        {
            agent = new AgentEntity { Id = heartbeat.AgentId };
            db.Agents.Add(agent);
        }
        agent.ComputerName = heartbeat.ComputerName;
        agent.AgentVersion = heartbeat.AgentVersion;
        agent.Site = heartbeat.Site;
        agent.IpAddress = heartbeat.IpAddress;
        agent.LastHeartbeatUtc = heartbeat.TimestampUtc;
        agent.PendingUploadCount = heartbeat.PendingUploadCount;
        agent.LocalDatabaseHealthy = heartbeat.LocalDatabaseHealthy;

        var deployment = await db.AgentDeployments.SingleOrDefaultAsync(x => x.AgentId == heartbeat.AgentId, cancellationToken);
        if (deployment is not null)
        {
            deployment.FirstHeartbeatAtUtc ??= heartbeat.TimestampUtc;
            deployment.LastHeartbeatAtUtc = heartbeat.TimestampUtc;
            deployment.State = AgentDeploymentLifecycleState.Online;
        }

        foreach (var status in heartbeat.Devices)
        {
            var device = await db.DeviceStatuses.SingleOrDefaultAsync(
                x => x.AgentId == heartbeat.AgentId && x.DeviceId == status.DeviceId, cancellationToken);
            if (device is null)
            {
                device = new DeviceStatusEntity { AgentId = heartbeat.AgentId, DeviceId = status.DeviceId };
                db.DeviceStatuses.Add(device);
            }
            device.Name = status.Name;
            device.State = status.State;
            device.QueueDepth = status.QueueDepth;
            device.TodaySuccess = status.TodaySuccess;
            device.TodayFailure = status.TodayFailure;
            device.LastProcessedAtUtc = status.LastProcessedAtUtc;
            device.LastError = Truncate(status.LastError, 2000);
            device.ObservedAtUtc = status.ObservedAtUtc;
        }

        if (heartbeat.Diagnostics is not null)
        {
            var snapshot = await db.AgentDiagnosticSnapshots.SingleOrDefaultAsync(x => x.AgentId == heartbeat.AgentId, cancellationToken);
            if (snapshot is null)
            {
                snapshot = new AgentDiagnosticSnapshotEntity { AgentId = heartbeat.AgentId };
                db.AgentDiagnosticSnapshots.Add(snapshot);
            }
            ApplyDiagnostics(snapshot, heartbeat.Diagnostics);
        }

        db.ProcessedRequests.Add(new ProcessedRequestEntity
        {
            Id = requestKey, AgentId = heartbeat.AgentId, Kind = "heartbeat", ProcessedAtUtc = DateTimeOffset.UtcNow
        });
        if (!heartbeat.LocalDatabaseHealthy)
            await EnsureAlertAsync(heartbeat.AgentId, null, "LOCAL_DB_UNHEALTHY", "Critical", "Agent本地数据库异常。", cancellationToken);
        if (heartbeat.PendingUploadCount >= 1000)
            await EnsureAlertAsync(heartbeat.AgentId, null, "OUTBOX_BACKLOG", "Warning", $"待上报队列积压 {heartbeat.PendingUploadCount} 条。", cancellationToken);
        foreach (var device in heartbeat.Devices.Where(x => x.State == RuntimeState.Faulted))
            await EnsureAlertAsync(heartbeat.AgentId, device.DeviceId, $"DEVICE_FAULT_{device.DeviceId}", "Critical", device.LastError ?? "设备采集异常。", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReceiveRecordsAsync(CollectionRecordBatch batch, CancellationToken cancellationToken)
    {
        var requestKey = RequestIdentity.Create(batch.AgentId, batch.RequestId);
        if (await db.ProcessedRequests.AnyAsync(x => x.Id == requestKey, cancellationToken)) return false;
        if (batch.Records.Any(x => !string.Equals(x.AgentId, batch.AgentId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("批次中的AgentId不一致。", nameof(batch));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existingIds = await db.CollectionRecords
            .Where(x => batch.Records.Select(r => r.RecordId).Contains(x.Id))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var existing = existingIds.ToHashSet();
        foreach (var record in batch.Records.Where(x => !existing.Contains(x.RecordId)))
        {
            db.CollectionRecords.Add(new CollectionRecordEntity
            {
                Id = record.RecordId, AgentId = record.AgentId, DeviceId = record.DeviceId,
                FileName = Truncate(record.FileName, 500) ?? "", Succeeded = record.Succeeded,
                RecordCount = record.RecordCount, DurationMilliseconds = record.DurationMilliseconds,
                ErrorCode = Truncate(record.ErrorCode, 100), ErrorMessage = Truncate(record.ErrorMessage, 2000),
                ProcessedAtUtc = record.ProcessedAtUtc
            });
        }
        db.ProcessedRequests.Add(new ProcessedRequestEntity
        {
            Id = requestKey, AgentId = batch.AgentId, Kind = "collection-records", ProcessedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<CommandEnvelope> CreateCommandAsync(string agentId, AgentCommandType type, string? deviceId,
        string parametersJson, DateTimeOffset expiresAtUtc, string actor, CancellationToken cancellationToken)
    {
        if (expiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(-1)) { /* valid for expiry tests and audit */ }
        var entity = new CommandEntity
        {
            Id = Guid.NewGuid(), AgentId = agentId, Type = type, DeviceId = deviceId,
            ParametersJson = string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson,
            CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = expiresAtUtc, CreatedBy = actor
        };
        db.Commands.Add(entity);
        db.AuditLogs.Add(new AuditLogEntity
        {
            Actor = actor, Action = "command.create", Target = $"{agentId}/{entity.Id}",
            Detail = type.ToString(), CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToContract(entity);
    }

    public async Task<List<CommandEnvelope>> GetPendingCommandsAsync(string agentId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        await db.Commands.AsNoTracking()
            .Where(x => x.AgentId == agentId && x.State == null && x.ExpiresAtUtc >= nowUtc)
            .OrderBy(x => x.CreatedAtUtc).Take(100).Select(x => new CommandEnvelope
            {
                CommandId = x.Id, AgentId = x.AgentId, Type = x.Type, DeviceId = x.DeviceId,
                ParametersJson = x.ParametersJson, CreatedAtUtc = x.CreatedAtUtc, ExpiresAtUtc = x.ExpiresAtUtc
            }).ToListAsync(cancellationToken);

    public async Task<bool> AcknowledgeCommandAsync(CommandAcknowledgement acknowledgement, CancellationToken cancellationToken)
    {
        var command = await db.Commands.SingleOrDefaultAsync(
            x => x.Id == acknowledgement.CommandId && x.AgentId == acknowledgement.AgentId, cancellationToken);
        if (command is null) return false;
        if (command.State is CommandExecutionState.Succeeded or CommandExecutionState.Failed or CommandExecutionState.Expired)
            return true;
        command.State = acknowledgement.State;
        command.AcknowledgedAtUtc = acknowledgement.TimestampUtc;
        command.Message = Truncate(acknowledgement.Message, 2000);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<ConfigPackage>> GetPendingConfigsAsync(string agentId, CancellationToken cancellationToken) =>
        await db.ConfigAssignments.AsNoTracking().Where(x => x.AgentId == agentId && x.State == null &&
                x.ConfigVersion.State == ConfigLifecycleState.Published)
            .OrderBy(x => x.CreatedAtUtc).Take(10).Select(x => new ConfigPackage
            {
                AssignmentId = x.Id, AgentId = x.AgentId, Version = x.ConfigVersion.Version,
                MinimumAgentVersion = x.ConfigVersion.MinimumAgentVersion, PayloadJson = x.ConfigVersion.PayloadJson,
                Sha256 = x.ConfigVersion.Sha256, CreatedAtUtc = x.CreatedAtUtc
            }).ToListAsync(cancellationToken);

    public async Task<bool> AcknowledgeConfigAsync(ConfigApplyResult result, CancellationToken cancellationToken)
    {
        var assignment = await db.ConfigAssignments.SingleOrDefaultAsync(
            x => x.Id == result.AssignmentId && x.AgentId == result.AgentId, cancellationToken);
        if (assignment is null) return false;
        assignment.State = result.State;
        assignment.AcknowledgedAtUtc = result.TimestampUtc;
        assignment.Message = Truncate(result.Message, 2000);
        assignment.EffectiveVersion = result.EffectiveVersion;
        assignment.EffectiveSha256 = Truncate(result.EffectiveSha256, 64);
        assignment.EffectiveObservedAtUtc = result.EffectiveObservedAtUtc;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static CommandEnvelope ToContract(CommandEntity x) => new()
    {
        CommandId = x.Id, AgentId = x.AgentId, Type = x.Type, DeviceId = x.DeviceId,
        ParametersJson = x.ParametersJson, CreatedAtUtc = x.CreatedAtUtc, ExpiresAtUtc = x.ExpiresAtUtc
    };

    private static string? Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) || value.Length <= length ? value : value[..length];

    private static void ApplyDiagnostics(AgentDiagnosticSnapshotEntity target, AgentRuntimeDiagnostics source)
    {
        target.ObservedAtUtc = source.ObservedAtUtc;
        target.IsWindowsService = source.IsWindowsService;
        target.ProcessStartedAtUtc = source.ProcessStartedAtUtc;
        target.ProcessPath = Truncate(source.ProcessPath, 1000);
        target.LocalDatabasePath = Truncate(source.LocalDatabasePath, 1000);
        target.LocalDatabaseState = source.LocalDatabaseState;
        target.LegacyDatabasePath = Truncate(source.LegacyDatabasePath, 1000);
        target.LegacyDatabaseExists = source.LegacyDatabaseExists;
        target.LegacyConfigPath = Truncate(source.LegacyConfigPath, 1000);
        target.LegacyConfigExists = source.LegacyConfigExists;
        target.LegacyExecutablePath = Truncate(source.LegacyExecutablePath, 1000);
        target.WinFormsProcessRunning = source.WinFormsProcessRunning;
        target.WinFormsLastSeenAtUtc = source.WinFormsLastSeenAtUtc;
        target.EffectiveConfigVersion = source.EffectiveConfigVersion;
        target.EffectiveConfigSha256 = Truncate(source.EffectiveConfigSha256, 64);
        target.LastErrorSummary = Truncate(source.LastErrorSummary, 2000);
    }

    private async Task EnsureAlertAsync(string agentId, string? deviceId, string code, string severity, string message, CancellationToken cancellationToken)
    {
        if (await db.Alerts.AnyAsync(x => x.AgentId == agentId && x.Code == code && x.AcknowledgedAtUtc == null, cancellationToken)) return;
        db.Alerts.Add(new AlertEntity
        {
            Id = Guid.NewGuid(), AgentId = agentId, DeviceId = deviceId, Code = code, Severity = severity,
            Message = Truncate(message, 2000) ?? "", CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
