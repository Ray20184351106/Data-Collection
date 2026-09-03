using System.Text.RegularExpressions;

namespace Acquisition.Agent.Setup;

public interface IDriveTypeProvider
{
    DriveType GetDriveType(string rootPath);
}

public sealed class SystemDriveTypeProvider : IDriveTypeProvider
{
    public DriveType GetDriveType(string rootPath) => new DriveInfo(rootPath).DriveType;
}

public sealed partial class DeploymentManifestValidator(IDriveTypeProvider driveTypes)
{
    private const int SupportedSchemaVersion = 1;
    private const int DefaultHeartbeatSeconds = 10;

    public ValidationResult<ValidatedDeployment> Validate(DeploymentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<string>();

        if (manifest.SchemaVersion != SupportedSchemaVersion)
            errors.Add($"deployment.json schemaVersion必须为{SupportedSchemaVersion}。");

        if (manifest.DeploymentId == Guid.Empty)
            errors.Add("deployment.json缺少DeploymentId。");

        var agentId = manifest.AgentId?.Trim() ?? "";
        if (!AgentIdPattern().IsMatch(agentId))
            errors.Add("AgentId只能包含字母、数字、点、下划线和短横线，长度为1至64个字符。");

        Uri? centerUri = null;
        if (!Uri.TryCreate(manifest.CenterBaseUrl?.Trim(), UriKind.Absolute, out centerUri) ||
            (centerUri.Scheme != Uri.UriSchemeHttp && centerUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(centerUri.UserInfo) ||
            IsWildcardHost(centerUri.Host))
        {
            errors.Add("CenterBaseUrl必须是可供设备访问的HTTP或HTTPS绝对地址，不能使用0.0.0.0等监听通配地址。");
        }

        var token = manifest.EnrollmentToken ?? "";
        if (token.Length is < 32 or > 512 || token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
            errors.Add("enrollment token格式无效。");
        if (manifest.EnrollmentExpiresAtUtc <= DateTimeOffset.UtcNow)
            errors.Add("enrollment token已过期，请在Center重新生成设备安装包。");

        var acquisitionDirectory = ValidateAcquisitionDirectory(manifest.AcquisitionAppDirectory, errors);
        var legacyExecutable = ValidateLegacyFilePath(manifest.LegacyExecutablePath, acquisitionDirectory, "采集程序", errors);
        var legacyDatabase = ValidateLegacyFilePath(manifest.LegacyDatabasePath, acquisitionDirectory, "采集记录库", errors);
        var legacyConfig = ValidateLegacyFilePath(manifest.LegacyConfigPath, acquisitionDirectory, "采集配置", errors);

        Version? version = null;
        if (!Version.TryParse(manifest.AgentVersion?.Trim(), out version) || version.Major < 0)
            errors.Add("Agent版本必须是可用于版本目录的数字版本，例如1.2.3。");

        if (errors.Count > 0 || centerUri is null || version is null || acquisitionDirectory is null ||
            legacyExecutable is null || legacyDatabase is null || legacyConfig is null)
            return ValidationResult<ValidatedDeployment>.Failure(errors);

        var normalizedCenter = new Uri(centerUri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + centerUri.AbsolutePath.TrimEnd('/'));
        return ValidationResult<ValidatedDeployment>.Success(new ValidatedDeployment(
            manifest.SchemaVersion,
            manifest.DeploymentId,
            agentId,
            normalizedCenter,
            token,
            manifest.EnrollmentExpiresAtUtc,
            manifest.Site?.Trim() ?? "",
            manifest.Building?.Trim() ?? "",
            manifest.Line?.Trim() ?? "",
            acquisitionDirectory,
            legacyExecutable,
            manifest.LocalDatabasePath,
            legacyDatabase,
            legacyConfig,
            manifest.InstallDirectory,
            manifest.StartWinFormsOnLogon,
            version,
            DefaultHeartbeatSeconds));
    }

    private string? ValidateAcquisitionDirectory(string? value, ICollection<string> errors)
    {
        var path = value?.Trim() ?? "";
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            errors.Add("采集程序目录不能使用UNC网络路径。");
            return null;
        }

        if (!Path.IsPathFullyQualified(path) || !LocalDrivePathPattern().IsMatch(path))
        {
            errors.Add("采集程序目录必须是本地磁盘上的绝对路径。");
            return null;
        }

        if (path.IndexOfAny(['\r', '\n', '"']) >= 0)
        {
            errors.Add("采集程序目录包含不允许的控制字符或引号。");
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("缺少驱动器根路径。");
            if (driveTypes.GetDriveType(root) == DriveType.Network)
            {
                errors.Add("采集程序目录不能位于映射网络驱动器。");
                return null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            errors.Add("无法确认采集程序目录所在的本地磁盘。");
            return null;
        }

        return fullPath;
    }

    private static string? ValidateLegacyFilePath(
        string? value,
        string? acquisitionDirectory,
        string displayName,
        ICollection<string> errors)
    {
        var path = value?.Trim() ?? "";
        if (acquisitionDirectory is null || !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOfAny(['\r', '\n', '"']) >= 0)
        {
            errors.Add($"{displayName}路径必须是采集程序目录内的本地绝对路径。");
            return null;
        }

        try
        {
            var normalized = Path.GetFullPath(path);
            var prefix = Path.TrimEndingDirectorySeparator(acquisitionDirectory) + Path.DirectorySeparatorChar;
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{displayName}路径必须位于采集程序目录内。");
                return null;
            }
            return normalized;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            errors.Add($"{displayName}路径无效。");
            return null;
        }
    }

    private static bool IsWildcardHost(string host) =>
        string.IsNullOrWhiteSpace(host) ||
        host is "0.0.0.0" or "::" or "*" or "+";

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentIdPattern();

    [GeneratedRegex("^[A-Za-z]:[\\\\/].+", RegexOptions.CultureInvariant)]
    private static partial Regex LocalDrivePathPattern();
}
