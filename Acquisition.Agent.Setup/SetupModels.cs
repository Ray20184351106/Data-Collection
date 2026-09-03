using System.Text.Json.Serialization;

namespace Acquisition.Agent.Setup;

public sealed record ValidatedDeployment(
    int SchemaVersion,
    Guid DeploymentId,
    string AgentId,
    Uri CenterBaseUri,
    string EnrollmentToken,
    DateTimeOffset EnrollmentExpiresAtUtc,
    string Site,
    string Building,
    string Line,
    string AcquisitionAppDirectory,
    string AcquisitionExecutablePath,
    string LocalDatabasePath,
    string LegacyDatabasePath,
    string LegacyConfigPath,
    string InstallDirectory,
    bool StartWinFormsOnLogon,
    Version AgentVersion,
    int HeartbeatSeconds);

public sealed class ValidationResult<T>
{
    private ValidationResult(T? value, IReadOnlyList<string> errors)
    {
        Value = value;
        Errors = errors;
    }

    public bool IsValid => Errors.Count == 0;
    public T? Value { get; }
    public IReadOnlyList<string> Errors { get; }

    public static ValidationResult<T> Success(T value) => new(value, Array.Empty<string>());
    public static ValidationResult<T> Failure(IEnumerable<string> errors) => new(default, errors.ToArray());
}

public sealed class PayloadManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("files")]
    public List<PayloadFileEntry> Files { get; init; } = [];
}

public sealed record PayloadFileEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("length")] long Length);

public sealed record FileCopyPlan(string SourcePath, string DestinationPath, long Length, string Sha256);

public sealed record InstallationPlan(
    string PackageRoot,
    string VersionDirectory,
    string ProgramDataDirectory,
    string AgentDatabasePath,
    string AgentAppSettingsPath,
    string EnrollmentTokenPath,
    string InstallStatusPath,
    string IdentityPath,
    string DiagnosticLogPath,
    string ServiceName,
    string ServiceDisplayName,
    string AgentExecutablePath,
    ValidatedDeployment Deployment,
    IReadOnlyList<FileCopyPlan> Files);

public sealed record ProcessCommandSpec(string FileName, IReadOnlyList<string> Arguments);

public sealed record CommandExecutionResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed record InstallationResult(bool Succeeded, string Message, string DiagnosticLogPath);
