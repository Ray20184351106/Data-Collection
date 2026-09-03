using Acquisition.Agent.Setup;

namespace Acquisition.Agent.Setup.Tests;

public sealed class InstallationExecutorTests
{
    [Fact]
    public async Task Successful_install_copies_only_planned_files_sets_shared_database_and_starts_service()
    {
        var plan = CreatePlan();
        var files = new RecordingInstallerFileSystem();
        var environment = new RecordingMachineEnvironmentStore { CurrentValue = @"C:\old-agent.db" };
        var services = new RecordingServiceManager();
        var executor = new InstallationExecutor(files, environment, services);

        var result = await executor.ExecuteAsync(plan, CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(plan.Files.Select(file => (file.SourcePath, file.DestinationPath)), files.Copies.Take(plan.Files.Count));
        Assert.True(files.TextWrites.TryGetValue(plan.AgentAppSettingsPath, out var appSettings));
        Assert.True(files.TextWrites.TryGetValue(plan.EnrollmentTokenPath, out var enrollment));
        Assert.Contains("\"agentId\": \"LINE01-PC01\"", appSettings, StringComparison.Ordinal);
        Assert.Contains("\"centerBaseUrl\": \"http://192.168.1.100:5080\"", appSettings, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Deployment.EnrollmentToken, appSettings, StringComparison.Ordinal);
        Assert.Contains(plan.Deployment.EnrollmentToken, enrollment, StringComparison.Ordinal);
        Assert.Equal(plan.AgentDatabasePath, environment.CurrentValue);
        Assert.True(services.InstallCalled);
        Assert.False(services.RemoveCalled);
        Assert.Empty(files.DeletedFiles);
    }

    [Fact]
    public async Task Service_failure_restores_environment_removes_only_new_service_and_preserves_diagnostics_without_token()
    {
        var plan = CreatePlan();
        var files = new RecordingInstallerFileSystem();
        var environment = new RecordingMachineEnvironmentStore { CurrentValue = @"C:\old-agent.db" };
        var services = new RecordingServiceManager { InstallException = new InvalidOperationException("service failed token-secret-value") };
        var executor = new InstallationExecutor(files, environment, services);

        var result = await executor.ExecuteAsync(plan, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(@"C:\old-agent.db", environment.CurrentValue);
        Assert.True(services.RemoveCalled);
        Assert.Empty(files.DeletedFiles);
        Assert.True(files.TextWrites.TryGetValue(plan.DiagnosticLogPath, out var diagnostic));
        Assert.DoesNotContain(plan.Deployment.EnrollmentToken, diagnostic, StringComparison.Ordinal);
        Assert.Contains("service failed", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Existing_service_stops_before_any_file_or_environment_change()
    {
        var plan = CreatePlan();
        var files = new RecordingInstallerFileSystem();
        var environment = new RecordingMachineEnvironmentStore { CurrentValue = @"C:\old-agent.db" };
        var services = new RecordingServiceManager { Exists = true };
        var executor = new InstallationExecutor(files, environment, services);

        var result = await executor.ExecuteAsync(plan, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(files.Copies);
        Assert.Equal(@"C:\old-agent.db", environment.CurrentValue);
        Assert.False(services.InstallCalled);
        Assert.False(services.RemoveCalled);
    }

    private static InstallationPlan CreatePlan()
    {
        const string token = "token-secret-value";
        var deployment = new ValidatedDeployment(
            1, Guid.NewGuid(), "LINE01-PC01", new Uri("http://192.168.1.100:5080"), token,
            DateTimeOffset.UtcNow.AddHours(1), "一厂", "一楼", "一号线", @"D:\采集程序",
            @"D:\采集程序\文件数据采集系统.exe", @"C:\ProgramData\AcquisitionAgent\agent.db",
            @"D:\采集程序\Data\采集记录.db", @"D:\采集程序\config.json", @"C:\Program Files\AcquisitionAgent",
            true, new Version(1, 2, 3), 10);
        return new InstallationPlan(
            @"E:\Package",
            @"C:\Program Files\AcquisitionAgent\versions\1.2.3",
            @"C:\ProgramData\AcquisitionAgent",
            @"C:\ProgramData\AcquisitionAgent\agent.db",
            @"C:\Program Files\AcquisitionAgent\versions\1.2.3\appsettings.json",
            @"C:\ProgramData\AcquisitionAgent\enrollment.json",
            @"C:\ProgramData\AcquisitionAgent\install-status.json",
            @"C:\ProgramData\AcquisitionAgent\identity.bin",
            @"C:\ProgramData\AcquisitionAgent\setup-diagnostic.log",
            "AcquisitionAgent",
            "文件数据采集 Agent",
            @"C:\Program Files\AcquisitionAgent\versions\1.2.3\Acquisition.Agent.exe",
            deployment,
            [new FileCopyPlan(@"E:\Package\AgentPayload\Acquisition.Agent.exe", @"C:\Program Files\AcquisitionAgent\versions\1.2.3\Acquisition.Agent.exe", 123, new string('a', 64))]);
    }

    private sealed class RecordingInstallerFileSystem : IInstallerFileSystem
    {
        public List<(string Source, string Destination)> Copies { get; } = [];
        public List<string> DeletedFiles { get; } = [];
        public Dictionary<string, string> TextWrites { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void CreateDirectory(string path) { }
        public void CopyFile(string sourcePath, string destinationPath) => Copies.Add((sourcePath, destinationPath));
        public void CreateEmptyFile(string path) { }
        public void WriteAllText(string path, string contents) => TextWrites[path] = contents;
    }

    private sealed class RecordingMachineEnvironmentStore : IMachineEnvironmentStore
    {
        public string? CurrentValue { get; set; }
        public string? Get(string name) => CurrentValue;
        public void Set(string name, string? value) => CurrentValue = value;
    }

    private sealed class RecordingServiceManager : IServiceManager
    {
        public bool Exists { get; init; }
        public Exception? InstallException { get; init; }
        public bool InstallCalled { get; private set; }
        public bool RemoveCalled { get; private set; }

        public Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken) => Task.FromResult(Exists);

        public Task InstallAndStartAsync(InstallationPlan plan, CancellationToken cancellationToken)
        {
            InstallCalled = true;
            return InstallException is null ? Task.CompletedTask : Task.FromException(InstallException);
        }

        public Task RemoveAsync(string serviceName, CancellationToken cancellationToken)
        {
            RemoveCalled = true;
            return Task.CompletedTask;
        }
    }
}
