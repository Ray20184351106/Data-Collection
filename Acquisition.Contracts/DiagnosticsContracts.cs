namespace Acquisition.Contracts;

public enum DiagnosticHealthState
{
    Unknown,
    Healthy,
    Unhealthy
}

public sealed class AgentRuntimeDiagnostics
{
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

public sealed class AppliedConfigState
{
    public int? Version { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public string? Sha256 => PayloadJson is null ? null : ConfigPackage.ComputeSha256(PayloadJson);
}
