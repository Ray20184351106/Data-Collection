using Acquisition.Center.Bootstrap;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Acquisition.Center.Configuration;

public sealed class EffectiveConfigurationService(IConfiguration configuration)
{
    public EffectiveCenterConfiguration Build(CenterBootstrapOptions bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        var runMode = ReadRunMode("Center:RunMode", bootstrap.RunMode);
        var bindAddress = ReadString("Center:BindAddress", bootstrap.BindAddress);
        var port = ReadInt("Center:Port", bootstrap.Port);
        var advertisedBaseUrl = ReadString("Center:AdvertisedBaseUrl", bootstrap.AdvertisedBaseUrl);
        var connectionString = configuration.GetConnectionString("CenterDatabase") ?? bootstrap.DatabaseConnectionString;
        var requireHttps = ReadBool("Security:RequireHttps", bootstrap.RequireHttps);
        var internalLanEnabled = ReadBool("InternalLan:Enabled", bootstrap.InternalLanEnabled);
        var compatibilityKey = configuration["InternalLan:RegistrationKey"] ?? bootstrap.CenterSharedCompatibilityKey;

        return new EffectiveCenterConfiguration
        {
            RunMode = runMode.ToString(),
            BindAddress = bindAddress,
            Port = port,
            AdvertisedBaseUrl = advertisedBaseUrl,
            RequireHttps = requireHttps,
            InternalLanEnabled = internalLanEnabled,
            Database = RedactDatabase(connectionString),
            DeploymentAdminAccessCode = new EffectiveSecretState(!string.IsNullOrWhiteSpace(bootstrap.DeploymentAdminAccessCodeHash)),
            CenterSharedCompatibilityKey = new EffectiveSecretState(!string.IsNullOrWhiteSpace(compatibilityKey)),
            Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runMode"] = ResolveSource("Center:RunMode"),
                ["bindAddress"] = ResolveSource("Center:BindAddress"),
                ["port"] = ResolveSource("Center:Port"),
                ["advertisedBaseUrl"] = ResolveSource("Center:AdvertisedBaseUrl"),
                ["database"] = ResolveSource("ConnectionStrings:CenterDatabase"),
                ["requireHttps"] = ResolveSource("Security:RequireHttps"),
                ["internalLanEnabled"] = ResolveSource("InternalLan:Enabled"),
                ["deploymentAdminAccessCode"] = "BootstrapFile",
                ["centerSharedCompatibilityKey"] = ResolveSource("InternalLan:RegistrationKey")
            }
        };
    }

    private CenterRunMode ReadRunMode(string key, CenterRunMode fallback) =>
        Enum.TryParse<CenterRunMode>(configuration[key], ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : fallback;

    private string ReadString(string key, string fallback) =>
        string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration[key]!.Trim();

    private int ReadInt(string key, int fallback) =>
        int.TryParse(configuration[key], out var value) ? value : fallback;

    private bool ReadBool(string key, bool fallback) =>
        bool.TryParse(configuration[key], out var value) ? value : fallback;

    private string ResolveSource(string key)
    {
        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                if (provider.TryGet(key, out var value) && value is not null)
                    return FriendlyProviderName(provider);
            }
        }
        return "BootstrapFile";
    }

    private static string FriendlyProviderName(IConfigurationProvider provider)
    {
        var name = provider.GetType().Name;
        if (name.Contains("EnvironmentVariables", StringComparison.Ordinal)) return "EnvironmentVariable";
        if (name.Contains("CommandLine", StringComparison.Ordinal)) return "CommandLine";
        if (name.Contains("Json", StringComparison.Ordinal)) return "JsonFile";
        if (name.Contains("Memory", StringComparison.Ordinal)) return "Memory";
        return name.Replace("ConfigurationProvider", "", StringComparison.Ordinal);
    }

    private static EffectiveDatabaseConfiguration RedactDatabase(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return new EffectiveDatabaseConfiguration
            {
                IsValid = !string.IsNullOrWhiteSpace(builder.DataSource) && !string.IsNullOrWhiteSpace(builder.InitialCatalog),
                Server = builder.DataSource,
                Database = builder.InitialCatalog,
                Authentication = builder.IntegratedSecurity ? "IntegratedSecurity" : "SqlServerLogin",
                Encryption = builder.Encrypt.ToString(),
                TrustServerCertificate = builder.TrustServerCertificate
            };
        }
        catch (ArgumentException)
        {
            return new EffectiveDatabaseConfiguration { IsValid = false };
        }
    }
}

public sealed class EffectiveCenterConfiguration
{
    public string RunMode { get; init; } = "";
    public string BindAddress { get; init; } = "";
    public int Port { get; init; }
    public string AdvertisedBaseUrl { get; init; } = "";
    public bool RequireHttps { get; init; }
    public bool InternalLanEnabled { get; init; }
    public EffectiveDatabaseConfiguration Database { get; init; } = new();
    public EffectiveSecretState DeploymentAdminAccessCode { get; init; } = new(false);
    public EffectiveSecretState CenterSharedCompatibilityKey { get; init; } = new(false);
    public IReadOnlyDictionary<string, string> Sources { get; init; } = new Dictionary<string, string>();
}

public sealed class EffectiveDatabaseConfiguration
{
    public bool IsValid { get; init; }
    public string Server { get; init; } = "";
    public string Database { get; init; } = "";
    public string Authentication { get; init; } = "";
    public string Encryption { get; init; } = "";
    public bool TrustServerCertificate { get; init; }
}

public sealed record EffectiveSecretState(bool IsConfigured)
{
    public string DisplayValue => IsConfigured ? "已配置（已隐藏）" : "未配置";
}
