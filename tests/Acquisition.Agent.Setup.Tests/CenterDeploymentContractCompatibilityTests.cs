using System.Text.Json;
using Acquisition.Agent.Setup;
using Acquisition.Contracts;

namespace Acquisition.Agent.Setup.Tests;

public sealed class CenterDeploymentContractCompatibilityTests
{
    [Fact]
    public void Setup_loads_the_exact_center_deployment_contract_without_a_parallel_manifest_shape()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent-setup-contract-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "deployment.json");
        Directory.CreateDirectory(root);
        var expected = new AgentDeploymentManifest
        {
            SchemaVersion = 1,
            DeploymentId = Guid.Parse("cd5ec4df-439c-48a9-b3cb-65287e0328a4"),
            AgentId = "LINE01-PC01",
            CenterBaseUrl = "http://192.168.1.100:5080",
            EnrollmentToken = "contract-token-that-is-long-enough-for-validation-123456",
            EnrollmentExpiresAtUtc = DateTimeOffset.Parse("2026-09-02T08:00:00+00:00"),
            Site = "一厂",
            Building = "一楼",
            Line = "一号线",
            AcquisitionAppDirectory = @"D:\采集程序",
            LocalDatabasePath = @"C:\ProgramData\AcquisitionAgent\agent.db",
            LegacyDatabasePath = @"D:\采集程序\Data\采集记录.db",
            LegacyConfigPath = @"D:\采集程序\config.json",
            LegacyExecutablePath = @"D:\采集程序\MachineDataAcquisitionSystem.exe",
            InstallDirectory = @"C:\Program Files\AcquisitionAgent",
            AgentVersion = "1.2.3",
            StartWinFormsOnLogon = true
        };

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(expected, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            AgentDeploymentManifest actual = new ManifestFileLoader().LoadDeployment(path);

            Assert.Equal(expected.DeploymentId, actual.DeploymentId);
            Assert.Equal(expected.EnrollmentExpiresAtUtc, actual.EnrollmentExpiresAtUtc);
            Assert.Equal(expected.LocalDatabasePath, actual.LocalDatabasePath);
            Assert.Equal(expected.LegacyExecutablePath, actual.LegacyExecutablePath);
            Assert.Equal(expected.InstallDirectory, actual.InstallDirectory);
            Assert.Equal(expected.StartWinFormsOnLogon, actual.StartWinFormsOnLogon);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }
}
