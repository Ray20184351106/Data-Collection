namespace Acquisition.Agent;

public sealed class AgentOptions
{
    public string AgentId { get; set; } = Environment.MachineName.ToLowerInvariant();
    public string CenterBaseUrl { get; set; } = "https://localhost:7443";
    public string RegistrationKey { get; set; } = "factory-registration-code";
    public string LocalDatabasePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AcquisitionAgent", "agent.db");
    public string LegacyDatabasePath { get; set; } = "";
    public string LegacyConfigPath { get; set; } = "";
    public string Site { get; set; } = "默认厂区";
    public int HeartbeatSeconds { get; set; } = 10;
}
