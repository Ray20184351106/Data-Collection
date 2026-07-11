using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Acquisition.Contracts;

public static class Protocol
{
    public const string Version = "1.0";
}

public enum RuntimeState { Stopped, Running, Degraded, Faulted }
public enum AgentCommandType { StartCollection, StopCollection, RefreshStatus, RetryFailedJob, ReloadApprovedConfig, CollectDiagnostics }
public enum CommandExecutionState { Received, Executing, Succeeded, Failed, Expired }
public enum ConfigApplyState { Downloaded, Validated, Applied, Failed, RolledBack }

public sealed class AgentHeartbeat
{
    public string AgentId { get; set; } = "";
    public Guid RequestId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string ProtocolVersion { get; set; } = Protocol.Version;
    public string ComputerName { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public string? Site { get; set; }
    public string? IpAddress { get; set; }
    public int PendingUploadCount { get; set; }
    public bool LocalDatabaseHealthy { get; set; }
    public List<DeviceRuntimeStatus> Devices { get; set; } = new();
}

public sealed class DeviceRuntimeStatus
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public RuntimeState State { get; set; }
    public int QueueDepth { get; set; }
    public int TodaySuccess { get; set; }
    public int TodayFailure { get; set; }
    public DateTimeOffset? LastProcessedAtUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed class CollectionRecordSummary
{
    public Guid RecordId { get; set; }
    public string AgentId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool Succeeded { get; set; }
    public int RecordCount { get; set; }
    public long DurationMilliseconds { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}

public sealed class CollectionRecordBatch
{
    public string AgentId { get; set; } = "";
    public Guid RequestId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string ProtocolVersion { get; set; } = Protocol.Version;
    public List<CollectionRecordSummary> Records { get; set; } = new();
}

public sealed class CommandEnvelope
{
    public Guid CommandId { get; set; }
    public string AgentId { get; set; } = "";
    public AgentCommandType Type { get; set; }
    public string? DeviceId { get; set; }
    public string ParametersJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }

    public bool CanExecuteAt(DateTimeOffset nowUtc) => nowUtc <= ExpiresAtUtc;
}

public sealed class CommandAcknowledgement
{
    public string AgentId { get; set; } = "";
    public Guid RequestId { get; set; }
    public Guid CommandId { get; set; }
    public CommandExecutionState State { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string? Message { get; set; }
}

public sealed class ConfigPackage
{
    public Guid AssignmentId { get; set; }
    public string AgentId { get; set; } = "";
    public int Version { get; set; }
    public string MinimumAgentVersion { get; set; } = "1.0.0";
    public string PayloadJson { get; set; } = "{}";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }

    public ValidationResult Validate()
    {
        var errors = new List<string>();
        if (Version <= 0) errors.Add("配置版本必须大于0。");
        if (string.IsNullOrWhiteSpace(AgentId)) errors.Add("AgentId不能为空。");
        if (PayloadJson.Contains("scriptCode", StringComparison.OrdinalIgnoreCase))
            errors.Add("首期配置禁止包含可执行脚本代码。");

        try { JsonDocument.Parse(PayloadJson).Dispose(); }
        catch (JsonException) { errors.Add("配置内容不是有效JSON。"); }

        var actualHash = ComputeSha256(PayloadJson);
        if (!string.Equals(actualHash, Sha256, StringComparison.OrdinalIgnoreCase))
            errors.Add("配置校验和不匹配。");
        return new ValidationResult(errors.Count == 0, errors);
    }

    public static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class ConfigApplyResult
{
    public string AgentId { get; set; } = "";
    public Guid RequestId { get; set; }
    public Guid AssignmentId { get; set; }
    public ConfigApplyState State { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string? Message { get; set; }
}

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed class ApiError
{
    public ApiErrorBody Error { get; set; } = new();
    public sealed class ApiErrorBody
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public object? Details { get; set; }
    }
}

public static class AgentPresence
{
    public static bool IsOnline(DateTimeOffset lastHeartbeatUtc, DateTimeOffset nowUtc) =>
        nowUtc - lastHeartbeatUtc <= TimeSpan.FromSeconds(30);
}

public static class RequestIdentity
{
    public static string Create(string agentId, Guid requestId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{agentId.Trim().ToLowerInvariant()}:{requestId:N}")))
            .ToLowerInvariant();
}
