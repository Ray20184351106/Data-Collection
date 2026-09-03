using Acquisition.Agent.Setup;
using Acquisition.Contracts;

namespace Acquisition.Agent.Setup.Tests;

public sealed class DeploymentManifestValidationTests
{
    [Fact]
    public void Valid_manifest_derives_legacy_paths_without_exposing_enrollment_token()
    {
        var manifest = ValidManifest();
        var validator = new DeploymentManifestValidator(new StubDriveTypeProvider(DriveType.Fixed));

        var result = validator.Validate(manifest);
        var summary = InstallationSummaryBuilder.Build(result.Value!);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(@"D:\采集程序\Data\采集记录.db", result.Value!.LegacyDatabasePath);
        Assert.Equal(@"D:\采集程序\config.json", result.Value.LegacyConfigPath);
        Assert.DoesNotContain(manifest.EnrollmentToken, string.Join(Environment.NewLine, summary));
        Assert.Contains(summary, line => line.Contains("LINE01-PC01", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("agent id with spaces")]
    [InlineData("../agent")]
    [InlineData("agent;sc delete AcquisitionAgent")]
    public void Invalid_agent_id_is_rejected(string agentId)
    {
        var manifest = ValidManifest();
        manifest.AgentId = agentId;
        var validator = new DeploymentManifestValidator(new StubDriveTypeProvider(DriveType.Fixed));

        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("AgentId", StringComparison.Ordinal));
    }

    [Fact]
    public void Unc_acquisition_directory_is_rejected()
    {
        var manifest = ValidManifest();
        manifest.AcquisitionAppDirectory = @"\\server\share\采集程序";
        var validator = new DeploymentManifestValidator(new StubDriveTypeProvider(DriveType.Network));

        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("UNC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Mapped_drive_is_rejected_even_when_path_is_fully_qualified()
    {
        var manifest = ValidManifest();
        var validator = new DeploymentManifestValidator(new StubDriveTypeProvider(DriveType.Network));

        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("映射", StringComparison.Ordinal));
    }

    [Fact]
    public void Wildcard_center_address_is_rejected_for_agent_package()
    {
        var manifest = ValidManifest();
        manifest.CenterBaseUrl = "http://0.0.0.0:5080";
        var validator = new DeploymentManifestValidator(new StubDriveTypeProvider(DriveType.Fixed));

        var result = validator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Center", StringComparison.Ordinal));
    }

    private static AgentDeploymentManifest ValidManifest() => new()
    {
        SchemaVersion = 1,
        AgentId = "LINE01-PC01",
        CenterBaseUrl = "http://192.168.1.100:5080",
        EnrollmentToken = "MDAxMjM0NTY3ODlhYmNkZWYwMTIzNDU2Nzg5YWJjZGVm",
        Site = "一厂",
        Building = "一楼",
        Line = "一号线",
        AcquisitionAppDirectory = @"D:\采集程序",
        DeploymentId = Guid.NewGuid(),
        EnrollmentExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        LocalDatabasePath = @"C:\ProgramData\AcquisitionAgent\agent.db",
        LegacyDatabasePath = @"D:\采集程序\Data\采集记录.db",
        LegacyConfigPath = @"D:\采集程序\config.json",
        LegacyExecutablePath = @"D:\采集程序\文件数据采集系统.exe",
        InstallDirectory = @"C:\Program Files\AcquisitionAgent",
        StartWinFormsOnLogon = true,
        AgentVersion = "1.2.3"
    };

    private sealed class StubDriveTypeProvider(DriveType driveType) : IDriveTypeProvider
    {
        public DriveType GetDriveType(string rootPath) => driveType;
    }
}
