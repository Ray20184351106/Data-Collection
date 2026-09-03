using Acquisition.Contracts;

namespace Acquisition.Center.Data;

public sealed class AgentDeploymentEntity
{
    public Guid Id { get; set; }
    public string AgentId { get; set; } = "";
    public string Site { get; set; } = "";
    public string Building { get; set; } = "";
    public string Line { get; set; } = "";
    public string AcquisitionAppDirectory { get; set; } = "";
    public bool StartWinFormsOnLogon { get; set; }
    public AgentDeploymentLifecycleState State { get; set; }
    public string EnrollmentTokenHash { get; set; } = "";
    public DateTimeOffset EnrollmentExpiresAtUtc { get; set; }
    public DateTimeOffset? EnrollmentConsumedAtUtc { get; set; }
    public string? DeviceCredentialHash { get; set; }
    public DateTimeOffset PackageIssuedAtUtc { get; set; }
    public DateTimeOffset? EnrolledAtUtc { get; set; }
    public DateTimeOffset? FirstHeartbeatAtUtc { get; set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class AgentDiagnosticSnapshotEntity
{
    public string AgentId { get; set; } = "";
    public DateTimeOffset ObservedAtUtc { get; set; }
    public bool? IsWindowsService { get; set; }
    public DateTimeOffset? ProcessStartedAtUtc { get; set; }
    public string? ProcessPath { get; set; }
    public string? LocalDatabasePath { get; set; }
    public DiagnosticHealthState LocalDatabaseState { get; set; }
    public string? LegacyDatabasePath { get; set; }
    public bool? LegacyDatabaseExists { get; set; }
    public string? LegacyConfigPath { get; set; }
    public bool? LegacyConfigExists { get; set; }
    public string? LegacyExecutablePath { get; set; }
    public bool? WinFormsProcessRunning { get; set; }
    public DateTimeOffset? WinFormsLastSeenAtUtc { get; set; }
    public int? EffectiveConfigVersion { get; set; }
    public string? EffectiveConfigSha256 { get; set; }
    public string? LastErrorSummary { get; set; }
}
