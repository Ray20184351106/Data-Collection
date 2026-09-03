using System.Text.Json;

namespace Acquisition.Agent.Setup;

public interface IInstallerFileSystem
{
    void CreateDirectory(string path);
    void CopyFile(string sourcePath, string destinationPath);
    void CreateEmptyFile(string path);
    void WriteAllText(string path, string contents);
}

public interface IMachineEnvironmentStore
{
    string? Get(string name);
    void Set(string name, string? value);
}

public interface IServiceManager
{
    Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken);
    Task InstallAndStartAsync(InstallationPlan plan, CancellationToken cancellationToken);
    Task RemoveAsync(string serviceName, CancellationToken cancellationToken);
}

public sealed class SystemInstallerFileSystem : IInstallerFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyFile(string sourcePath, string destinationPath)
    {
        var parent = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("目标文件缺少父目录。");
        Directory.CreateDirectory(parent);
        File.Copy(sourcePath, destinationPath, overwrite: false);
    }

    public void CreateEmptyFile(string path)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("一次性注册文件缺少父目录。");
        Directory.CreateDirectory(parent);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    public void WriteAllText(string path, string contents)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("诊断文件缺少父目录。");
        Directory.CreateDirectory(parent);
        File.WriteAllText(path, contents);
    }
}

public sealed class SystemMachineEnvironmentStore : IMachineEnvironmentStore
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);

    public void Set(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Machine);
}

public sealed class InstallationExecutor(
    IInstallerFileSystem fileSystem,
    IMachineEnvironmentStore environment,
    IServiceManager services,
    IFileAccessControlManager? accessControl = null)
{
    private const string SharedDatabaseEnvironmentName = "ACQUISITION_AGENT_DB";
    private readonly IFileAccessControlManager _accessControl = accessControl ?? new NoOpFileAccessControlManager();

    public async Task<InstallationResult> ExecuteAsync(InstallationPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (await services.ExistsAsync(plan.ServiceName, cancellationToken))
        {
            return new InstallationResult(
                false,
                "已存在AcquisitionAgent服务；一期安装器不会覆盖现有安装。",
                plan.DiagnosticLogPath);
        }

        var previousDatabasePath = environment.Get(SharedDatabaseEnvironmentName);
        var serviceInstallAttempted = false;
        try
        {
            fileSystem.CreateDirectory(plan.VersionDirectory);
            fileSystem.CreateDirectory(plan.ProgramDataDirectory);
            foreach (var file in plan.Files)
                fileSystem.CopyFile(file.SourcePath, file.DestinationPath);

            fileSystem.WriteAllText(plan.AgentAppSettingsPath, BuildAgentAppSettings(plan));
            fileSystem.CreateEmptyFile(plan.EnrollmentTokenPath);
            await _accessControl.ProtectEnrollmentAsync(plan.EnrollmentTokenPath, cancellationToken);
            fileSystem.WriteAllText(plan.EnrollmentTokenPath, BuildEnrollmentFile(plan));
            environment.Set(SharedDatabaseEnvironmentName, plan.AgentDatabasePath);
            serviceInstallAttempted = true;
            await services.InstallAndStartAsync(plan, cancellationToken);
            return new InstallationResult(true, "Agent文件和Windows服务已安装并启动。", plan.DiagnosticLogPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var cleanupMessages = new List<string>();
            if (serviceInstallAttempted)
            {
                try { await services.RemoveAsync(plan.ServiceName, CancellationToken.None); }
                catch (Exception cleanupError) { cleanupMessages.Add("服务回退失败：" + cleanupError.Message); }
            }
            try { environment.Set(SharedDatabaseEnvironmentName, previousDatabasePath); }
            catch (Exception cleanupError) { cleanupMessages.Add("环境变量回退失败：" + cleanupError.Message); }

            var diagnostic = BuildDiagnostic(plan, ex, cleanupMessages);
            try { fileSystem.WriteAllText(plan.DiagnosticLogPath, diagnostic); }
            catch { /* 安装失败不能被诊断日志写入失败覆盖。 */ }
            return new InstallationResult(
                false,
                "安装失败；新文件保留在版本目录供诊断，未递归删除任何目录。",
                plan.DiagnosticLogPath);
        }
    }

    private static string BuildDiagnostic(InstallationPlan plan, Exception error, IReadOnlyCollection<string> cleanupMessages)
    {
        var message = error.ToString();
        if (!string.IsNullOrEmpty(plan.Deployment.EnrollmentToken))
            message = message.Replace(plan.Deployment.EnrollmentToken, "[REDACTED]", StringComparison.Ordinal);
        var cleanup = cleanupMessages.Count == 0 ? "无额外回退错误。" : string.Join(Environment.NewLine, cleanupMessages);
        if (!string.IsNullOrEmpty(plan.Deployment.EnrollmentToken))
            cleanup = cleanup.Replace(plan.Deployment.EnrollmentToken, "[REDACTED]", StringComparison.Ordinal);
        return $"时间：{DateTimeOffset.Now:O}{Environment.NewLine}" +
               $"AgentId：{plan.Deployment.AgentId}{Environment.NewLine}" +
               $"版本目录：{plan.VersionDirectory}{Environment.NewLine}" +
               $"错误：{message}{Environment.NewLine}" +
               $"回退：{cleanup}{Environment.NewLine}" +
               "说明：安装器保留已复制文件用于诊断，不会递归删除目录。";
    }

    private static string BuildAgentAppSettings(InstallationPlan plan) => JsonSerializer.Serialize(new
    {
        Agent = new
        {
            plan.Deployment.AgentId,
            CenterBaseUrl = plan.Deployment.CenterBaseUri.AbsoluteUri.TrimEnd('/'),
            RegistrationKey = "",
            LocalDatabasePath = plan.AgentDatabasePath,
            plan.Deployment.LegacyDatabasePath,
            plan.Deployment.LegacyConfigPath,
            LegacyExecutablePath = plan.Deployment.AcquisitionExecutablePath,
            IdentityPath = plan.IdentityPath,
            EnrollmentTokenPath = plan.EnrollmentTokenPath,
            InstallStatusPath = plan.InstallStatusPath,
            plan.Deployment.Site,
            HeartbeatSeconds = plan.Deployment.HeartbeatSeconds
        }
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    private static string BuildEnrollmentFile(InstallationPlan plan) => JsonSerializer.Serialize(new
    {
        plan.Deployment.AgentId,
        plan.Deployment.EnrollmentToken
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
}
