using Acquisition.Agent.Services;
using Acquisition.Agent.Storage;
using Acquisition.Contracts;

namespace Acquisition.Agent;

public sealed class AgentWorker(
    AgentOptions options,
    AgentLocalStore store,
    CenterClient center,
    LegacyCommandExecutor commands,
    ConfigApplicator configs,
    LegacyRecordImporter importer,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await store.InitializeAsync(stoppingToken);
        logger.LogInformation("Agent {AgentId} 已启动。", options.AgentId);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(options.HeartbeatSeconds, 5, 60)));
        do
        {
            try
            {
                await importer.ImportAsync(stoppingToken);
                await UploadRecordsAsync(stoppingToken);
                await ProcessCommandsAsync(stoppingToken);
                await ProcessConfigsAsync(stoppingToken);
                await SendHeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("中心通信失败，将保留本地数据后重试：{Message}", ex.Message); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var heartbeat = new AgentHeartbeat
        {
            AgentId = options.AgentId, RequestId = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow,
            ComputerName = Environment.MachineName, AgentVersion = typeof(AgentWorker).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Site = options.Site, PendingUploadCount = await store.CountPendingRecordsAsync(cancellationToken),
            LocalDatabaseHealthy = true, Devices = await store.GetDeviceStatusesAsync(cancellationToken)
        };
        await center.SendHeartbeatAsync(heartbeat, cancellationToken);
    }

    private async Task UploadRecordsAsync(CancellationToken cancellationToken)
    {
        var records = await store.GetPendingRecordsAsync(500, cancellationToken);
        if (records.Count == 0) return;
        var batch = new CollectionRecordBatch
        {
            AgentId = options.AgentId, RequestId = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow, Records = records
        };
        if (await center.SendRecordsAsync(batch, cancellationToken)) await store.MarkRecordsSentAsync(records.Select(x => x.RecordId), cancellationToken);
    }

    private async Task ProcessCommandsAsync(CancellationToken cancellationToken)
    {
        foreach (var command in await center.GetCommandsAsync(cancellationToken))
        {
            if (!await store.TryClaimCommandAsync(command.CommandId, cancellationToken))
            {
                var saved = await store.GetCommandResultAsync(command.CommandId, cancellationToken);
                if (saved.HasValue && saved.Value.State is CommandExecutionState.Succeeded or CommandExecutionState.Failed or CommandExecutionState.Expired)
                    await AcknowledgeCommandAsync(command.CommandId, saved.Value.State, saved.Value.Message, cancellationToken);
                continue;
            }
            var state = CommandExecutionState.Failed; string message;
            if (!command.CanExecuteAt(DateTimeOffset.UtcNow)) { state = CommandExecutionState.Expired; message = "命令已过期。"; }
            else
            {
                var result = await commands.ExecuteAsync(command, cancellationToken);
                state = result.Succeeded ? CommandExecutionState.Succeeded : CommandExecutionState.Failed; message = result.Message;
            }
            await store.CompleteCommandAsync(command.CommandId, state, message, cancellationToken);
            await AcknowledgeCommandAsync(command.CommandId, state, message, cancellationToken);
        }
    }

    private Task AcknowledgeCommandAsync(Guid commandId, CommandExecutionState state, string? message, CancellationToken cancellationToken) =>
        center.AcknowledgeCommandAsync(new CommandAcknowledgement
        {
            AgentId = options.AgentId, RequestId = Guid.NewGuid(), CommandId = commandId,
            State = state, TimestampUtc = DateTimeOffset.UtcNow, Message = message
        }, cancellationToken);

    private async Task ProcessConfigsAsync(CancellationToken cancellationToken)
    {
        foreach (var package in await center.GetConfigsAsync(cancellationToken))
        {
            var result = await configs.ApplyAsync(package, cancellationToken);
            var state = result.IsValid ? ConfigApplyState.Applied : ConfigApplyState.RolledBack;
            await center.AcknowledgeConfigAsync(new ConfigApplyResult
            {
                AgentId = options.AgentId, RequestId = Guid.NewGuid(), AssignmentId = package.AssignmentId,
                State = state, TimestampUtc = DateTimeOffset.UtcNow,
                Message = result.IsValid ? "配置已原子应用。" : string.Join("; ", result.Errors)
            }, cancellationToken);
        }
    }
}
