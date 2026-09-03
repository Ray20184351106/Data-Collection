using Acquisition.Agent.Setup;
using Acquisition.Contracts;

namespace Acquisition.Agent.Setup.Tests;

public sealed class InstallationPlannerTests
{
    [Fact]
    public void Plan_uses_versioned_program_files_and_program_data_paths()
    {
        var validated = new ValidatedDeployment(
            1,
            Guid.NewGuid(),
            "LINE01-PC01",
            new Uri("http://192.168.1.100:5080"),
            "token-not-for-display",
            DateTimeOffset.UtcNow.AddHours(1),
            "一厂",
            "一楼",
            "一号线",
            @"D:\采集程序",
            @"D:\采集程序\文件数据采集系统.exe",
            @"C:\ProgramData\AcquisitionAgent\agent.db",
            @"D:\采集程序\Data\采集记录.db",
            @"D:\采集程序\config.json",
            @"C:\Program Files\AcquisitionAgent",
            true,
            new Version(1, 2, 3),
            10);
        var payload = new PayloadManifest
        {
            SchemaVersion = 1,
            Files =
            [
                new PayloadFileEntry("AgentPayload/Acquisition.Agent.exe", new string('a', 64), 123),
                new PayloadFileEntry("AgentPayload/Acquisition.Agent.dll", new string('b', 64), 456),
                new PayloadFileEntry("AgentSetup.exe", new string('c', 64), 789)
            ]
        };

        var plan = new InstallationPlanner(
            @"C:\Program Files",
            @"C:\ProgramData").Create(@"E:\Package", validated, payload);

        Assert.Equal(@"C:\Program Files\AcquisitionAgent\versions\1.2.3", plan.VersionDirectory);
        Assert.Equal(@"C:\ProgramData\AcquisitionAgent\agent.db", plan.AgentDatabasePath);
        Assert.Equal(@"C:\Program Files\AcquisitionAgent\versions\1.2.3\appsettings.json", plan.AgentAppSettingsPath);
        Assert.Equal(@"C:\ProgramData\AcquisitionAgent\enrollment.json", plan.EnrollmentTokenPath);
        Assert.Equal("AcquisitionAgent", plan.ServiceName);
        Assert.Equal(2, plan.Files.Count);
        Assert.Equal(@"C:\Program Files\AcquisitionAgent\versions\1.2.3\Acquisition.Agent.exe", plan.AgentExecutablePath);
        Assert.All(plan.Files, file => Assert.StartsWith(plan.VersionDirectory, file.DestinationPath, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("1.2.3/../../Windows")]
    [InlineData("latest")]
    public void Deployment_version_that_cannot_be_a_version_directory_is_rejected(string version)
    {
        var manifest = new AgentDeploymentManifest
        {
            SchemaVersion = 1,
            AgentId = "LINE01-PC01",
            CenterBaseUrl = "http://192.168.1.100:5080",
            EnrollmentToken = new string('x', 40),
            AcquisitionAppDirectory = @"D:\采集程序",
            AgentVersion = version,
            EnrollmentExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

        var result = new DeploymentManifestValidator(new FixedDriveProvider()).Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("版本", StringComparison.Ordinal));
    }

    private sealed class FixedDriveProvider : IDriveTypeProvider
    {
        public DriveType GetDriveType(string rootPath) => DriveType.Fixed;
    }
}
