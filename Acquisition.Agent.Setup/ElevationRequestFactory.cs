using System.Diagnostics;
using System.Security.Principal;

namespace Acquisition.Agent.Setup;

public sealed record ElevationLaunchRequest(
    string FileName,
    string WorkingDirectory,
    string Verb,
    bool UseShellExecute,
    IReadOnlyList<string> Arguments);

public static class ElevationRequestFactory
{
    public static ElevationLaunchRequest Create(string executablePath, string workingDirectory)
    {
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.IndexOfAny(['\r', '\n', '"']) >= 0)
            throw new ArgumentException("安装器程序路径无效。", nameof(executablePath));
        if (!Path.IsPathFullyQualified(workingDirectory) || workingDirectory.IndexOfAny(['\r', '\n', '"']) >= 0)
            throw new ArgumentException("安装器工作目录无效。", nameof(workingDirectory));
        return new ElevationLaunchRequest(executablePath, workingDirectory, "runas", true, ["--elevated"]);
    }
}

public static class WindowsElevation
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void Relaunch(ElevationLaunchRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            Verb = request.Verb,
            UseShellExecute = request.UseShellExecute
        };
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);
        Process.Start(startInfo);
    }
}
