namespace Acquisition.Center.Configuration;

public sealed class CenterRuntimeOptions
{
    public bool InternalLanEnabled { get; init; }
    public bool RequireHttps { get; init; }
    public bool AllowLegacySharedRegistrationKey { get; init; }
    public string CompatibilityRegistrationKey { get; init; } = "";
}
