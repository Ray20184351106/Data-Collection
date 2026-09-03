namespace Acquisition.Contracts;

public enum AgentDeploymentLifecycleState
{
    PackageIssued,
    Enrolled,
    Online,
    Offline
}

public sealed class CreateAgentDeploymentRequest
{
    public string AgentId { get; set; } = "";
    public string Site { get; set; } = "";
    public string Building { get; set; } = "";
    public string Line { get; set; } = "";
    public string AcquisitionAppDirectory { get; set; } = "";
    public bool StartWinFormsOnLogon { get; set; }
}

public sealed class AgentDeploymentManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Guid DeploymentId { get; set; }
    public string AgentId { get; set; } = "";
    public string CenterBaseUrl { get; set; } = "";
    public string EnrollmentToken { get; set; } = "";
    public DateTimeOffset EnrollmentExpiresAtUtc { get; set; }
    public string Site { get; set; } = "";
    public string Building { get; set; } = "";
    public string Line { get; set; } = "";
    public string AcquisitionAppDirectory { get; set; } = "";
    public string LocalDatabasePath { get; set; } = @"C:\ProgramData\AcquisitionAgent\agent.db";
    public string LegacyDatabasePath { get; set; } = "";
    public string LegacyConfigPath { get; set; } = "";
    public string LegacyExecutablePath { get; set; } = "";
    public string InstallDirectory { get; set; } = @"C:\Program Files\AcquisitionAgent";
    public string AgentVersion { get; set; } = "1.0.0";
    public bool StartWinFormsOnLogon { get; set; }
}

public sealed class AgentPayloadManifest
{
    public int SchemaVersion { get; set; } = 1;
    public List<AgentPayloadFile> Files { get; set; } = new();
}

public sealed class AgentPayloadFile
{
    public string Path { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class AgentEnrollmentRequest
{
    public string AgentId { get; set; } = "";
    public string EnrollmentToken { get; set; } = "";
}

public sealed class AgentEnrollmentResponse
{
    public string AgentId { get; set; } = "";
    public string DeviceCredential { get; set; } = "";
    public DateTimeOffset EnrolledAtUtc { get; set; }
}

public sealed class AgentDeploymentSummary
{
    public Guid Id { get; set; }
    public string AgentId { get; set; } = "";
    public string Site { get; set; } = "";
    public string Building { get; set; } = "";
    public string Line { get; set; } = "";
    public string AcquisitionAppDirectory { get; set; } = "";
    public bool StartWinFormsOnLogon { get; set; }
    public AgentDeploymentLifecycleState State { get; set; }
    public DateTimeOffset PackageIssuedAtUtc { get; set; }
    public DateTimeOffset? EnrolledAtUtc { get; set; }
    public DateTimeOffset? FirstHeartbeatAtUtc { get; set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
}
