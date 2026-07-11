using Acquisition.Agent.Storage;
using Acquisition.Contracts;
using Xunit;

namespace Acquisition.Agent.Tests;

public sealed class AgentLocalStoreIntegrationTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.db");
    private AgentLocalStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new AgentLocalStore(_path);
        await _store.InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task Outbox_keeps_unsent_records_until_confirmed()
    {
        var first = new CollectionRecordSummary { RecordId = Guid.NewGuid(), AgentId = "agent-01", DeviceId = "1", FileName = "a.txt" };
        var second = new CollectionRecordSummary { RecordId = Guid.NewGuid(), AgentId = "agent-01", DeviceId = "1", FileName = "b.txt" };
        await _store.EnqueueRecordAsync(first, CancellationToken.None);
        await _store.EnqueueRecordAsync(second, CancellationToken.None);

        var pending = await _store.GetPendingRecordsAsync(100, CancellationToken.None);
        await _store.MarkRecordsSentAsync(new[] { first.RecordId }, CancellationToken.None);

        Assert.Equal(2, pending.Count);
        Assert.Equal(1, await _store.CountPendingRecordsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Command_id_is_claimed_only_once()
    {
        var commandId = Guid.NewGuid();

        Assert.True(await _store.TryClaimCommandAsync(commandId, CancellationToken.None));
        Assert.False(await _store.TryClaimCommandAsync(commandId, CancellationToken.None));
    }

    [Fact]
    public async Task Completed_command_result_can_be_replayed_to_center()
    {
        var commandId = Guid.NewGuid();
        Assert.True(await _store.TryClaimCommandAsync(commandId, CancellationToken.None));
        await _store.CompleteCommandAsync(commandId, CommandExecutionState.Succeeded, "done", CancellationToken.None);

        var saved = await _store.GetCommandResultAsync(commandId, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(CommandExecutionState.Succeeded, saved.Value.State);
        Assert.Equal("done", saved.Value.Message);
    }
}
