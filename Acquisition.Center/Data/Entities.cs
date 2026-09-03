using Acquisition.Contracts;

namespace Acquisition.Center.Data;

public sealed class AgentEntity
{
    public string Id { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public string? Site { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset LastHeartbeatUtc { get; set; }
    public int PendingUploadCount { get; set; }
    public bool LocalDatabaseHealthy { get; set; }
}

public sealed class DeviceStatusEntity
{
    public long Id { get; set; }
    public string AgentId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public RuntimeState State { get; set; }
    public int QueueDepth { get; set; }
    public int TodaySuccess { get; set; }
    public int TodayFailure { get; set; }
    public DateTimeOffset? LastProcessedAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? ObservedAtUtc { get; set; }
}

public sealed class ProcessedRequestEntity
{
    public string Id { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Kind { get; set; } = "";
    public DateTimeOffset ProcessedAtUtc { get; set; }
}

public sealed class CollectionRecordEntity
{
    public Guid Id { get; set; }
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

public sealed class CommandEntity
{
    public Guid Id { get; set; }
    public string AgentId { get; set; } = "";
    public AgentCommandType Type { get; set; }
    public string? DeviceId { get; set; }
    public string ParametersJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public CommandExecutionState? State { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public string? Message { get; set; }
    public string CreatedBy { get; set; } = "system";
}

public enum ConfigLifecycleState { Published }

public sealed class ConfigVersionEntity
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public string MinimumAgentVersion { get; set; } = "1.0.0";
    public string PayloadJson { get; set; } = "{}";
    public string Sha256 { get; set; } = "";
    public Guid? RequestId { get; set; }
    public string PreviewSha256 { get; set; } = "";
    public string? Reason { get; set; }
    public Guid? RollbackSourceVersionId { get; set; }
    public ConfigLifecycleState State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class ConfigAssignmentEntity
{
    public Guid Id { get; set; }
    public Guid ConfigVersionId { get; set; }
    public string AgentId { get; set; } = "";
    public ConfigApplyState? State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public string? Message { get; set; }
    public int? EffectiveVersion { get; set; }
    public string? EffectiveSha256 { get; set; }
    public DateTimeOffset? EffectiveObservedAtUtc { get; set; }
    public ConfigVersionEntity ConfigVersion { get; set; } = null!;
}

public sealed class AuditLogEntity
{
    public long Id { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Detail { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class AlertEntity
{
    public Guid Id { get; set; }
    public string AgentId { get; set; } = "";
    public string? DeviceId { get; set; }
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "Warning";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
}
