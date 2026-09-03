using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Acquisition.Agent.Setup;

public sealed partial class PayloadIntegrityVerifier
{
    public ValidationResult<IReadOnlyList<string>> Verify(string packageRoot, PayloadManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<string>();
        var verified = new List<string>();
        if (manifest.SchemaVersion != 1) errors.Add("payload.manifest.json schemaVersion必须为1。");
        if (manifest.Files.Count == 0) errors.Add("payload.manifest.json没有文件条目。");

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var rootPrefix = root + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasAgentExecutable = false;
        var hasSetupExecutable = false;

        foreach (var entry in manifest.Files)
        {
            if (!TryNormalizePayloadPath(entry.Path, out var normalized, out var pathError))
            {
                errors.Add(pathError!);
                continue;
            }
            if (!paths.Add(normalized!))
            {
                errors.Add($"payload文件路径重复：{entry.Path}");
                continue;
            }
            if (Path.GetFileName(normalized!).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"payload不能包含运行时配置文件：{entry.Path}");
                continue;
            }
            if (!HashPattern().IsMatch(entry.Sha256 ?? "") || entry.Length < 0)
            {
                errors.Add($"payload文件hash或长度无效：{entry.Path}");
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, normalized!));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"payload文件路径越过安装包边界：{entry.Path}");
                continue;
            }
            if (!File.Exists(fullPath))
            {
                errors.Add($"payload文件不存在：{entry.Path}");
                continue;
            }

            var info = new FileInfo(fullPath);
            if (info.Length != entry.Length)
            {
                errors.Add($"payload文件长度不匹配：{entry.Path}");
                continue;
            }
            using var stream = File.OpenRead(fullPath);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(entry.Sha256!),
                    Convert.FromHexString(actual)))
            {
                errors.Add($"payload文件SHA-256不匹配：{entry.Path}");
                continue;
            }

            if (string.Equals(normalized, Path.Combine("AgentPayload", "Acquisition.Agent.exe"), StringComparison.OrdinalIgnoreCase))
                hasAgentExecutable = true;
            if (string.Equals(normalized, "AgentSetup.exe", StringComparison.OrdinalIgnoreCase))
                hasSetupExecutable = true;
            verified.Add(fullPath);
        }

        if (!hasAgentExecutable) errors.Add("payload缺少AgentPayload/Acquisition.Agent.exe。");
        if (!hasSetupExecutable) errors.Add("payload缺少AgentSetup.exe。");
        return errors.Count == 0
            ? ValidationResult<IReadOnlyList<string>>.Success(verified)
            : ValidationResult<IReadOnlyList<string>>.Failure(errors);
    }

    internal static bool TryNormalizePayloadPath(string? value, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        var path = value?.Trim() ?? "";
        if (path.Length == 0 || Path.IsPathFullyQualified(path) || path.IndexOf(':') >= 0)
        {
            error = $"payload文件路径必须是相对路径：{value}";
            return false;
        }

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            error = $"payload文件路径无效：{value}";
            return false;
        }

        if (segments.Length == 1 && string.Equals(segments[0], "AgentSetup.exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = segments[0];
            return true;
        }

        if (!string.Equals(segments[0], "AgentPayload", StringComparison.OrdinalIgnoreCase) || segments.Length < 2)
        {
            error = $"payload文件路径无效：{value}";
            return false;
        }

        normalized = Path.Combine(segments);
        return true;
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
