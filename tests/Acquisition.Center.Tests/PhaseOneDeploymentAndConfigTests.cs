using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Acquisition.Center.Data;
using Acquisition.Center.Services;
using Acquisition.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Acquisition.Center.Tests;

public sealed class PhaseOneDeploymentAndConfigTests : IAsyncLifetime
{
    private readonly string _databaseName = $"AcquisitionCenterPhaseOne_{Guid.NewGuid():N}";
    private readonly string _assetRoot = Path.Combine(Path.GetTempPath(), $"center-assets-{Guid.NewGuid():N}");
    private CenterDbContext _db = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_assetRoot, "AgentPayload"));
        await File.WriteAllTextAsync(Path.Combine(_assetRoot, "AgentSetup.exe"), "setup-binary");
        await File.WriteAllTextAsync(Path.Combine(_assetRoot, "AgentPayload", "Acquisition.Agent.exe"), "agent-binary");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Integrated Security=true;TrustServerCertificate=true";
        _db = new CenterDbContext(new DbContextOptionsBuilder<CenterDbContext>().UseSqlServer(connection).Options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
        File.Delete(Path.Combine(_assetRoot, "AgentPayload", "Acquisition.Agent.exe"));
        File.Delete(Path.Combine(_assetRoot, "AgentSetup.exe"));
        Directory.Delete(Path.Combine(_assetRoot, "AgentPayload"));
        Directory.Delete(_assetRoot);
    }

    [Fact]
    public async Task Enrollment_token_is_stored_only_as_a_hash_and_can_be_consumed_once()
    {
        var deployments = new AgentDeploymentService(_db, new DeploymentPackageOptions
        {
            AdvertisedBaseUrl = "http://192.168.1.100:5080",
            SetupExecutablePath = Path.Combine(_assetRoot, "AgentSetup.exe"),
            AgentPayloadPath = Path.Combine(_assetRoot, "AgentPayload"),
            AgentVersion = "1.0.0"
        });
        var package = await deployments.CreatePackageAsync(new CreateAgentDeploymentRequest
        {
            AgentId = "LINE01-PC01", Site = "一厂", Building = "一楼", Line = "1号线",
            AcquisitionAppDirectory = @"D:\采集程序", StartWinFormsOnLogon = true
        }, "operator", CancellationToken.None);

        AgentDeploymentManifest manifest;
        using (var archive = new ZipArchive(new MemoryStream(package.Content), ZipArchiveMode.Read))
        await using (var stream = archive.GetEntry("deployment.json")!.Open())
            manifest = (await JsonSerializer.DeserializeAsync<AgentDeploymentManifest>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

        var stored = await _db.AgentDeployments.SingleAsync();
        Assert.NotEqual(manifest.EnrollmentToken, stored.EnrollmentTokenHash);
        Assert.DoesNotContain(manifest.EnrollmentToken, JsonSerializer.Serialize(stored));

        var enrollment = new AgentEnrollmentService(_db);
        var identity = await enrollment.EnrollAsync(new AgentEnrollmentRequest
        {
            AgentId = manifest.AgentId,
            EnrollmentToken = manifest.EnrollmentToken
        }, CancellationToken.None);

        Assert.True(await new AgentCredentialService(_db).IsValidAsync(manifest.AgentId, identity.DeviceCredential, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => enrollment.EnrollAsync(new AgentEnrollmentRequest
        {
            AgentId = manifest.AgentId,
            EnrollmentToken = manifest.EnrollmentToken
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Preview_hash_blocks_changed_publish_and_rollback_creates_a_higher_version()
    {
        _db.Agents.Add(new AgentEntity
        {
            Id = "agent-01", ComputerName = "PC-01", AgentVersion = "1.0.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow, LocalDatabaseHealthy = true
        });
        await _db.SaveChangesAsync();
        var service = new ConfigManagementService(_db);
        var previewRequest = Request(@"D:\Line\M1\Incoming");
        var preview = await service.PreviewAsync(previewRequest, CancellationToken.None);

        var changed = Request(@"D:\Line\M1\Changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(new PublishConfigVersionRequest
        {
            RequestId = Guid.NewGuid(), Name = changed.Name, MinimumAgentVersion = changed.MinimumAgentVersion,
            AgentIds = changed.AgentIds, Machines = changed.Machines, PreviewSha256 = preview.PreviewSha256
        }, "operator", CancellationToken.None));

        var first = await service.PublishAsync(new PublishConfigVersionRequest
        {
            RequestId = Guid.NewGuid(), Name = previewRequest.Name, MinimumAgentVersion = previewRequest.MinimumAgentVersion,
            AgentIds = previewRequest.AgentIds, Machines = previewRequest.Machines, PreviewSha256 = preview.PreviewSha256
        }, "operator", CancellationToken.None);
        var rollback = await service.RollbackAsync(first.Id, new RollbackConfigVersionRequest
        {
            RequestId = Guid.NewGuid(), AgentIds = new() { "agent-01" }, Reason = "试点恢复"
        }, "operator", CancellationToken.None);

        Assert.True(rollback.Version > first.Version);
        Assert.Equal(first.Id, rollback.RollbackSourceVersionId);
        Assert.Equal(first.PayloadJson, rollback.PayloadJson);
    }

    [Fact]
    public async Task Rollback_rejects_a_request_id_already_used_by_a_publish()
    {
        _db.Agents.Add(new AgentEntity
        {
            Id = "agent-01", ComputerName = "PC-01", AgentVersion = "1.0.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow, LocalDatabaseHealthy = true
        });
        await _db.SaveChangesAsync();
        var service = new ConfigManagementService(_db);
        var request = Request(@"D:\Line\M1\Incoming");
        var preview = await service.PreviewAsync(request, CancellationToken.None);
        var requestId = Guid.NewGuid();
        var published = await service.PublishAsync(new PublishConfigVersionRequest
        {
            RequestId = requestId, Name = request.Name, MinimumAgentVersion = request.MinimumAgentVersion,
            AgentIds = request.AgentIds, Machines = request.Machines, PreviewSha256 = preview.PreviewSha256
        }, "operator", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RollbackAsync(
            published.Id,
            new RollbackConfigVersionRequest
            {
                RequestId = requestId, AgentIds = new() { "agent-01" }, Reason = "不应复用发布请求号"
            },
            "operator",
            CancellationToken.None));
    }

    [Fact]
    public async Task Deployment_package_rejects_a_reparse_point_directory()
    {
        var externalRoot = Path.Combine(Path.GetTempPath(), $"center-assets-external-{Guid.NewGuid():N}");
        var linkPath = Path.Combine(_assetRoot, "AgentPayload", "linked-content");
        Directory.CreateDirectory(externalRoot);
        await File.WriteAllTextAsync(Path.Combine(externalRoot, "outside.txt"), "must-not-be-packaged");
        CreateDirectoryLink(linkPath, externalRoot);
        try
        {
            var deployments = new AgentDeploymentService(_db, new DeploymentPackageOptions
            {
                AdvertisedBaseUrl = "http://192.168.1.100:5080",
                SetupExecutablePath = Path.Combine(_assetRoot, "AgentSetup.exe"),
                AgentPayloadPath = Path.Combine(_assetRoot, "AgentPayload"),
                AgentVersion = "1.0.0"
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => deployments.CreatePackageAsync(
                new CreateAgentDeploymentRequest
                {
                    AgentId = "LINE01-PC01", Site = "一厂", Building = "一楼", Line = "1号线",
                    AcquisitionAppDirectory = @"D:\采集程序", StartWinFormsOnLogon = true
                },
                "operator",
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(linkPath);
            File.Delete(Path.Combine(externalRoot, "outside.txt"));
            Directory.Delete(externalRoot);
        }
    }

    private static ConfigPreviewRequest Request(string monitorPath) => new()
    {
        Name = "试点配置", MinimumAgentVersion = "1.0.0", AgentIds = new() { "agent-01" },
        Machines = new()
        {
            new ManagedMachineConfiguration
            {
                Id = 1, Name = "机台1", MonitorPath = monitorPath,
                SuccessPath = @"D:\Line\M1\Success", ErrorPath = @"D:\Line\M1\Error"
            }
        }
    };

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "/d", "/c", "mklink", "/J", linkPath, targetPath }
        }) ?? throw new InvalidOperationException("无法启动目录联接创建命令。");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"无法创建测试目录联接：{process.StandardError.ReadToEnd()}");
    }
}
