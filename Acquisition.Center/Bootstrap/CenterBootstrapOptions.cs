namespace Acquisition.Center.Bootstrap;

public enum CenterRunMode
{
    IsolatedTest,
    Production
}

public sealed class CenterBootstrapDraft
{
    public CenterRunMode RunMode { get; set; } = CenterRunMode.IsolatedTest;
    public string BindAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5080;
    public string AdvertisedBaseUrl { get; set; } = "http://127.0.0.1:5080";
    public string DatabaseConnectionString { get; set; } = "";
    public bool InternalLanEnabled { get; set; } = true;
    public bool RequireHttps { get; set; }
}

public sealed class CenterBootstrapOptions
{
    public int SchemaVersion { get; set; } = 1;
    public CenterRunMode RunMode { get; set; } = CenterRunMode.IsolatedTest;
    public string BindAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5080;
    public string AdvertisedBaseUrl { get; set; } = "http://127.0.0.1:5080";
    public string DatabaseConnectionString { get; set; } = "";
    public bool InternalLanEnabled { get; set; } = true;
    public bool RequireHttps { get; set; }
    public string DeploymentAdminAccessCodeAlgorithm { get; set; } = "PBKDF2-HMAC-SHA256";
    public int DeploymentAdminAccessCodeIterations { get; set; }
    public string DeploymentAdminAccessCodeSalt { get; set; } = "";
    public string DeploymentAdminAccessCodeHash { get; set; } = "";
    public string CenterSharedCompatibilityKey { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    internal CenterBootstrapDraft ToDraft() => new()
    {
        RunMode = RunMode,
        BindAddress = BindAddress,
        Port = Port,
        AdvertisedBaseUrl = AdvertisedBaseUrl,
        DatabaseConnectionString = DatabaseConnectionString,
        InternalLanEnabled = InternalLanEnabled,
        RequireHttps = RequireHttps
    };
}

public sealed record CenterBootstrapCreationResult(string DeploymentAdminAccessCode);
