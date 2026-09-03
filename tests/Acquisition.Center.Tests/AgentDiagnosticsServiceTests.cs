using System.IO.Compression;
using System.Text.Json;
using Acquisition.Center.Data;
using Acquisition.Center.Services;
using Acquisition.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Acquisition.Center.Tests;

public sealed class AgentDiagnosticsServiceTests : IAsyncLifetime
{
    private readonly string _databaseName = $"AcquisitionCenterDiagnostics_{Guid.NewGuid():N}";
    private CenterDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Integrated Security=true;TrustServerCertificate=true";
        _db = new CenterDbContext(new DbContextOptionsBuilder<CenterDbContext>().UseSqlServer(connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Agents.Add(new AgentEntity
        {
            Id = "LINE01-PC01", ComputerName = "LINE01-PC01", AgentVersion = "1.0.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow, LocalDatabaseHealthy = true
        });
        _db.AgentDiagnosticSnapshots.Add(new AgentDiagnosticSnapshotEntity
        {
            AgentId = "LINE01-PC01", ObservedAtUtc = DateTimeOffset.UtcNow,
            LocalDatabaseState = DiagnosticHealthState.Healthy,
            LocalDatabasePath = @"D:\Factory\TopSecret\agent.db",
            LegacyConfigPath = @"D:\Factory\TopSecret\config.json",
            EffectiveConfigVersion = 4,
            EffectiveConfigSha256 = "b03f4dc82c1d0ed4e6211d8d0b3974d9cce7aee2cdd9972bf93a0fbaf2976b1c"
        });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Diagnostics_export_redacts_full_paths_and_all_hash_values()
    {
        var service = new AgentDiagnosticsService(_db);

        var export = await service.ExportAsync("line01-pc01", CancellationToken.None);

        Assert.NotNull(export);
        using var archive = new ZipArchive(new MemoryStream(export!.Content), ZipArchiveMode.Read);
        await using var stream = archive.GetEntry("diagnostics.json")!.Open();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        Assert.DoesNotContain(@"D:\Factory\TopSecret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("b03f4dc82c1d0ed4e6211d8d0b3974d9cce7aee2cdd9972bf93a0fbaf2976b1c", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256", json, StringComparison.OrdinalIgnoreCase);
    }
}
