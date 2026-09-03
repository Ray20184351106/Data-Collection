namespace Acquisition.Agent.Setup;

public sealed class AccessControlCommandFactory(string windowsDirectory)
{
    private readonly string _icaclsExe = Path.Combine(windowsDirectory, "System32", "icacls.exe");

    public ProcessCommandSpec CreateProtectEnrollmentCommand(string enrollmentPath)
    {
        if (!Path.IsPathFullyQualified(enrollmentPath) ||
            enrollmentPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            enrollmentPath.IndexOfAny(['\r', '\n', '"']) >= 0)
        {
            throw new ArgumentException("一次性注册文件路径无效。", nameof(enrollmentPath));
        }

        return new ProcessCommandSpec(
            _icaclsExe,
            [enrollmentPath, "/inheritance:r", "/grant:r", "*S-1-5-18:F", "*S-1-5-32-544:F"]);
    }
}

public interface IFileAccessControlManager
{
    Task ProtectEnrollmentAsync(string enrollmentPath, CancellationToken cancellationToken);
}

public sealed class IcaclsFileAccessControlManager(
    AccessControlCommandFactory commands,
    IProcessRunner runner) : IFileAccessControlManager
{
    public async Task ProtectEnrollmentAsync(string enrollmentPath, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(commands.CreateProtectEnrollmentCommand(enrollmentPath), cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException($"无法限制部署凭据文件权限（退出码{result.ExitCode}）。");
    }
}

internal sealed class NoOpFileAccessControlManager : IFileAccessControlManager
{
    public Task ProtectEnrollmentAsync(string enrollmentPath, CancellationToken cancellationToken) => Task.CompletedTask;
}
