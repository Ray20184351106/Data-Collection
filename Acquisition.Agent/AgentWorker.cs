using System.Diagnostics;
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
        var devices = await store.GetDeviceStatusesAsync(cancellationToken);
        var databaseHealthy = await store.CheckHealthAsync(cancellationToken);
        var applied = await store.GetAppliedConfigStateAsync(cancellationToken);
        var heartbeat = new AgentHeartbeat
        {
            AgentId = options.AgentId, RequestId = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow,
            ComputerName = Environment.MachineName, AgentVersion = typeof(AgentWorker).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Site = options.Site, PendingUploadCount = await store.CountPendingRecordsAsync(cancellationToken),
            LocalDatabaseHealthy = databaseHealthy, Devices = devices,
            Diagnostics = BuildDiagnostics(databaseHealthy, devices, applied)
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
            var effective = await store.GetAppliedConfigStateAsync(cancellationToken);
            await center.AcknowledgeConfigAsync(new ConfigApplyResult
            {
                AgentId = options.AgentId, RequestId = Guid.NewGuid(), AssignmentId = package.AssignmentId,
                State = state, TimestampUtc = DateTimeOffset.UtcNow,
                Message = result.IsValid ? "配置已原子应用。" : string.Join("; ", result.Errors),
                EffectiveVersion = effective.Version,
                EffectiveSha256 = effective.Sha256,
                EffectiveObservedAtUtc = effective.UpdatedAtUtc
            }, cancellationToken);
        }
    }

    private AgentRuntimeDiagnostics BuildDiagnostics(
        bool databaseHealthy, IReadOnlyCollection<DeviceRuntimeStatus> devices, AppliedConfigState applied)
    {
        bool? winFormsRunning = null;
        if (!string.IsNullOrWhiteSpace(options.LegacyExecutablePath))
        {
            var processName = Path.GetFileNameWithoutExtension(options.LegacyExecutablePath);
            var processes = Process.GetProcessesByName(processName);
            try { winFormsRunning = processes.Length > 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        DateTimeOffset? processStartedAtUtc = null;
        string? processPath = null;
        try
        {
            using var process = Process.GetCurrentProcess();
            processStartedAtUtc = process.StartTime.ToUniversalTime();
            processPath = Environment.ProcessPath;
        }
        catch { /* 诊断字段未知不能影响心跳。 */ }
        return new AgentRuntimeDiagnostics
        {
            ObservedAtUtc = DateTimeOffset.UtcNow,
            IsWindowsService = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService(),
            ProcessStartedAtUtc = processStartedAtUtc,
            ProcessPath = processPath,
            LocalDatabasePath = options.LocalDatabasePath,
            LocalDatabaseState = databaseHealthy ? DiagnosticHealthState.Healthy : DiagnosticHealthState.Unhealthy,
            LegacyDatabasePath = NullIfBlank(options.LegacyDatabasePath),
            LegacyDatabaseExists = FileState(options.LegacyDatabasePath),
            LegacyConfigPath = NullIfBlank(options.LegacyConfigPath),
            LegacyConfigExists = FileState(options.LegacyConfigPath),
            LegacyExecutablePath = NullIfBlank(options.LegacyExecutablePath),
            WinFormsProcessRunning = winFormsRunning,
            WinFormsLastSeenAtUtc = devices.Select(x => x.ObservedAtUtc).Where(x => x.HasValue).Max(),
            EffectiveConfigVersion = applied.Version,
            EffectiveConfigSha256 = applied.Sha256,
            LastErrorSummary = devices.Select(x => x.LastError).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
        };
    }

    private static bool? FileState(string? path) => string.IsNullOrWhiteSpace(path) ? null : File.Exists(path);
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
