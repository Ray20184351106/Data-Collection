using System.Text.RegularExpressions;

namespace Acquisition.Agent.Setup;

public sealed partial class ServiceCommandFactory(string windowsDirectory)
{
    private readonly string _scExe = Path.Combine(windowsDirectory, "System32", "sc.exe");

    public IReadOnlyList<ProcessCommandSpec> CreateInstallCommands(string serviceName, string displayName, string executablePath)
    {
        ValidateServiceName(serviceName);
        ValidateDisplayName(displayName);
        ValidateExecutablePath(executablePath);
        var quotedBinaryPath = $"\"{Path.GetFullPath(executablePath)}\"";

        return
        [
            new(_scExe, ["create", serviceName, "binPath=", quotedBinaryPath, "start=", "delayed-auto", "DisplayName=", displayName]),
            new(_scExe, ["description", serviceName, "文件数据采集设备端通信与离线补传服务"]),
            new(_scExe, ["failure", serviceName, "reset=", "86400", "actions=", "restart/5000/restart/30000/restart/60000"]),
            new(_scExe, ["failureflag", serviceName, "1"]),
            new(_scExe, ["start", serviceName])
        ];
    }

    public ProcessCommandSpec CreateQueryCommand(string serviceName)
    {
        ValidateServiceName(serviceName);
        return new ProcessCommandSpec(_scExe, ["query", serviceName]);
    }

    public IReadOnlyList<ProcessCommandSpec> CreateRemovalCommands(string serviceName)
    {
        ValidateServiceName(serviceName);
        return
        [
            new ProcessCommandSpec(_scExe, ["stop", serviceName]),
            new ProcessCommandSpec(_scExe, ["delete", serviceName])
        ];
    }

    private static void ValidateServiceName(string serviceName)
    {
        if (!ServiceNamePattern().IsMatch(serviceName ?? ""))
            throw new ArgumentException("Windows服务名格式无效。", nameof(serviceName));
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 128 || displayName.Any(char.IsControl))
            throw new ArgumentException("Windows服务显示名格式无效。", nameof(displayName));
    }

    private static void ValidateExecutablePath(string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.StartsWith(@"\\", StringComparison.Ordinal) ||
            executablePath.IndexOfAny(['\r', '\n', '"']) >= 0)
            throw new ArgumentException("Windows服务程序路径无效。", nameof(executablePath));
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNamePattern();
}
