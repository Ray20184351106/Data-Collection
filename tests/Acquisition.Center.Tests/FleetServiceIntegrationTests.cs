using Acquisition.Center.Data;
using Acquisition.Center.Services;
using Acquisition.Center.Security;
using Acquisition.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Acquisition.Center.Tests;

public sealed class FleetServiceIntegrationTests : IAsyncLifetime
{
    private readonly string _databaseName = $"AcquisitionCenterTests_{Guid.NewGuid():N}";
    private string _connectionString = null!;
    private CenterDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<CenterDbContext>().UseSqlServer(_connectionString).Options;
        _db = new CenterDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Duplicate_heartbeat_request_is_idempotent_and_updates_one_agent()
    {
        var requestId = Guid.NewGuid();
        var heartbeat = new AgentHeartbeat
        {
            AgentId = "agent-01", RequestId = requestId, TimestampUtc = DateTimeOffset.UtcNow,
            ComputerName = "DEVICE-PC-01", AgentVersion = "1.0.0", LocalDatabaseHealthy = true
        };
        var service = new FleetService(_db);

        Assert.True(await service.ReceiveHeartbeatAsync(heartbeat, CancellationToken.None));
        Assert.False(await service.ReceiveHeartbeatAsync(heartbeat, CancellationToken.None));

        Assert.Equal(1, await _db.Agents.CountAsync());
        Assert.Equal(1, await _db.ProcessedRequests.CountAsync());
    }

    [Fact]
    public async Task Database_bootstrap_succeeds_for_fresh_internal_schema()
    {
        await DatabaseBootstrapper.InitializeAsync(_db);

        Assert.True(await _db.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Expired_commands_are_not_returned_to_agent()
    {
        var service = new FleetService(_db);
        await service.CreateCommandAsync("agent-01", AgentCommandType.RefreshStatus, null, "{}", DateTimeOffset.UtcNow.AddSeconds(-1), "admin", CancellationToken.None);

        var commands = await service.GetPendingCommandsAsync("agent-01", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Empty(commands);
    }

    [Fact]
    public async Task Fifty_agents_can_send_heartbeats_concurrently()
    {
        var tasks = Enumerable.Range(1, 50).Select(async index =>
        {
            var options = new DbContextOptionsBuilder<CenterDbContext>().UseSqlServer(_connectionString).Options;
            await using var db = new CenterDbContext(options);
            var service = new FleetService(db);
            await service.ReceiveHeartbeatAsync(new AgentHeartbeat
            {
                AgentId = $"load-agent-{index:00}", RequestId = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow,
                ComputerName = $"LOAD-PC-{index:00}", AgentVersion = "1.0.0", LocalDatabaseHealthy = true
            }, CancellationToken.None);
        });

        await Task.WhenAll(tasks);

        Assert.Equal(50, await _db.Agents.CountAsync());
    }

    [Fact]
    public async Task Internal_sync_publishes_config_to_all_agents_immediately()
    {
        var service = new ConfigManagementService(_db);

        var config = await service.SyncInternalAsync(
            "line-config", "{\"machines\":[]}", "1.0.0", new[] { "agent-01", "agent-02" }, "internal-admin", CancellationToken.None);

        Assert.Equal(ConfigLifecycleState.Published, config.State);
        Assert.Equal(2, await _db.ConfigAssignments.CountAsync(x => x.ConfigVersionId == config.Id));
    }

    [Theory]
    [InlineData("factory-key", "factory-key", true)]
    [InlineData("factory-key", "wrong-key", false)]
    [InlineData("factory-key", null, false)]
    public void Registration_key_must_match(string configured, string? supplied, bool expected)
    {
        Assert.Equal(expected, InternalLanCredentialValidator.IsValid(configured, supplied));
    }
}
