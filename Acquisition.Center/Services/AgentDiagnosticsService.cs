using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acquisition.Center.Data;
using Acquisition.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Acquisition.Center.Services;

public sealed record AgentDiagnosticCheck(string Name, DiagnosticHealthState State, string Message);

public sealed class AgentDiagnosticView
{
    public string AgentId { get; init; } = "";
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public bool IsOnline { get; init; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; init; }
    public string? AgentVersion { get; init; }
    public string? ComputerName { get; init; }
    public AgentDeploymentLifecycleState? DeploymentState { get; init; }
    public AgentRuntimeDiagnostics? Runtime { get; init; }
    public IReadOnlyList<AgentDiagnosticCheck> Checks { get; init; } = Array.Empty<AgentDiagnosticCheck>();
}

public sealed record AgentDiagnosticExport(string FileName, byte[] Content);

public sealed class AgentDiagnosticsService(CenterDbContext db)
{
    private const int MaximumExportBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AgentDiagnosticView?> GetAsync(string agentId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeAgentId(agentId);
        var agent = await db.Agents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == normalized, cancellationToken);
        var deployment = await db.AgentDeployments.AsNoTracking().SingleOrDefaultAsync(x => x.AgentId == normalized, cancellationToken);
        var snapshot = await db.AgentDiagnosticSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.AgentId == normalized, cancellationToken);
        if (agent is null && deployment is null && snapshot is null) return null;

        var now = DateTimeOffset.UtcNow;
        var lastHeartbeat = deployment?.LastHeartbeatAtUtc ?? agent?.LastHeartbeatUtc;
        var online = lastHeartbeat.HasValue && AgentPresence.IsOnline(lastHeartbeat.Value, now);
        var runtime = snapshot is null ? null : ToRuntime(snapshot);
        return new AgentDiagnosticView
        {
            AgentId = normalized,
            GeneratedAtUtc = now,
            ObservedAtUtc = snapshot?.ObservedAtUtc ?? lastHeartbeat,
            IsOnline = online,
            LastHeartbeatAtUtc = lastHeartbeat,
            AgentVersion = agent?.AgentVersion,
            ComputerName = agent?.ComputerName,
            DeploymentState = deployment is null
                ? null
                : online ? AgentDeploymentLifecycleState.Online
                    : deployment.EnrolledAtUtc.HasValue ? AgentDeploymentLifecycleState.Offline : deployment.State,
            Runtime = runtime,
            Checks = BuildChecks(agent, deployment, snapshot, online)
        };
    }

    public async Task<AgentDiagnosticExport?> ExportAsync(string agentId, CancellationToken cancellationToken)
    {
        var view = await GetAsync(agentId, cancellationToken);
        if (view is null) return null;
        var safeView = Redact(view);
        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(stream, safeView, JsonOptions, cancellationToken);
        }
        if (output.Length > MaximumExportBytes)
            throw new InvalidOperationException("诊断导出超过10MB上限。");
        return new AgentDiagnosticExport($"{SafeFileName(view.AgentId)}-diagnostics.zip", output.ToArray());
    }

    private static IReadOnlyList<AgentDiagnosticCheck> BuildChecks(
        AgentEntity? agent,
        AgentDeploymentEntity? deployment,
        AgentDiagnosticSnapshotEntity? snapshot,
        bool online)
    {
        var checks = new List<AgentDiagnosticCheck>
        {
            new("心跳", online ? DiagnosticHealthState.Healthy : DiagnosticHealthState.Unhealthy,
                online ? "Agent心跳在30秒窗口内。" : "Agent心跳已超过30秒或尚未上报。"),
            new("设备身份", deployment?.EnrolledAtUtc.HasValue == true ? DiagnosticHealthState.Healthy : DiagnosticHealthState.Unknown,
                deployment?.EnrolledAtUtc.HasValue == true ? "Agent已使用设备独立凭据注册。" : "未观察到一期设备注册记录。"),
            new("本地状态库", snapshot?.LocalDatabaseState ?? DiagnosticHealthState.Unknown,
                snapshot is null ? "Agent尚未上报运行诊断。"
                    : snapshot.LocalDatabaseState == DiagnosticHealthState.Healthy ? "Agent本地状态库可读写。"
                    : snapshot.LocalDatabaseState == DiagnosticHealthState.Unhealthy ? "Agent本地状态库异常。" : "Agent本地状态库状态未知。"),
            new("采集程序", snapshot?.WinFormsProcessRunning switch
            {
                true => DiagnosticHealthState.Healthy,
                false => DiagnosticHealthState.Unhealthy,
                _ => DiagnosticHealthState.Unknown
            }, snapshot?.WinFormsProcessRunning switch
            {
                true => "已观察到WinForms采集进程。",
                false => "未观察到WinForms采集进程。",
                _ => "WinForms采集进程状态未知。"
            }),
            new("最终生效配置", snapshot?.EffectiveConfigVersion.HasValue == true && !string.IsNullOrWhiteSpace(snapshot.EffectiveConfigSha256)
                    ? DiagnosticHealthState.Healthy : DiagnosticHealthState.Unknown,
                snapshot?.EffectiveConfigVersion.HasValue == true && !string.IsNullOrWhiteSpace(snapshot.EffectiveConfigSha256)
                    ? $"Agent已回报生效配置 v{snapshot.EffectiveConfigVersion}。" : "Agent尚未回报最终生效配置版本与校验和。")
        };
        if (agent is not null && !agent.LocalDatabaseHealthy)
            checks.Add(new("心跳数据库标志", DiagnosticHealthState.Unhealthy, "最近心跳报告本地数据库异常。"));
        return checks;
    }

    private static AgentRuntimeDiagnostics ToRuntime(AgentDiagnosticSnapshotEntity value) => new()
    {
        ObservedAtUtc = value.ObservedAtUtc,
        IsWindowsService = value.IsWindowsService,
        ProcessStartedAtUtc = value.ProcessStartedAtUtc,
        ProcessPath = value.ProcessPath,
        LocalDatabasePath = value.LocalDatabasePath,
        LocalDatabaseState = value.LocalDatabaseState,
        LegacyDatabasePath = value.LegacyDatabasePath,
        LegacyDatabaseExists = value.LegacyDatabaseExists,
        LegacyConfigPath = value.LegacyConfigPath,
        LegacyConfigExists = value.LegacyConfigExists,
        LegacyExecutablePath = value.LegacyExecutablePath,
        WinFormsProcessRunning = value.WinFormsProcessRunning,
        WinFormsLastSeenAtUtc = value.WinFormsLastSeenAtUtc,
        EffectiveConfigVersion = value.EffectiveConfigVersion,
        EffectiveConfigSha256 = value.EffectiveConfigSha256,
        LastErrorSummary = Truncate(value.LastErrorSummary, 1000)
    };

    private static AgentDiagnosticView Redact(AgentDiagnosticView source)
    {
        var runtime = source.Runtime;
        return new AgentDiagnosticView
        {
            AgentId = source.AgentId,
            GeneratedAtUtc = source.GeneratedAtUtc,
            ObservedAtUtc = source.ObservedAtUtc,
            IsOnline = source.IsOnline,
            LastHeartbeatAtUtc = source.LastHeartbeatAtUtc,
            AgentVersion = source.AgentVersion,
            ComputerName = source.ComputerName,
            DeploymentState = source.DeploymentState,
            Checks = source.Checks,
            Runtime = runtime is null ? null : new AgentRuntimeDiagnostics
            {
                ObservedAtUtc = runtime.ObservedAtUtc,
                IsWindowsService = runtime.IsWindowsService,
                ProcessStartedAtUtc = runtime.ProcessStartedAtUtc,
                ProcessPath = RedactPath(runtime.ProcessPath),
                LocalDatabasePath = RedactPath(runtime.LocalDatabasePath),
                LocalDatabaseState = runtime.LocalDatabaseState,
                LegacyDatabasePath = RedactPath(runtime.LegacyDatabasePath),
                LegacyDatabaseExists = runtime.LegacyDatabaseExists,
                LegacyConfigPath = RedactPath(runtime.LegacyConfigPath),
                LegacyConfigExists = runtime.LegacyConfigExists,
                LegacyExecutablePath = RedactPath(runtime.LegacyExecutablePath),
                WinFormsProcessRunning = runtime.WinFormsProcessRunning,
                WinFormsLastSeenAtUtc = runtime.WinFormsLastSeenAtUtc,
                EffectiveConfigVersion = runtime.EffectiveConfigVersion,
                // The dashboard can compare the checksum for a signed-in operator.
                // Offline exports intentionally omit all hash material.
                EffectiveConfigSha256 = null,
                LastErrorSummary = Truncate(runtime.LastErrorSummary, 500)
            }
        };
    }

    private static string? RedactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        try
        {
            var root = Path.GetPathRoot(path) ?? "";
            return string.IsNullOrWhiteSpace(root) ? Path.GetFileName(path) : Path.Combine(root, "[redacted]", Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return "[redacted]";
        }
    }

    private static string NormalizeAgentId(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId)) throw new ArgumentException("AgentId不能为空。", nameof(agentId));
        return agentId.Trim().ToUpperInvariant();
    }

    private static string SafeFileName(string value) =>
        string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_'));

    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maximumLength ? value : value[..maximumLength];
}
