using Acquisition.Contracts;
using Xunit;

namespace Acquisition.Contracts.Tests;

public sealed class ContractBehaviorTests
{
    [Fact]
    public void Command_cannot_execute_after_expiration()
    {
        var command = new CommandEnvelope
        {
            CommandId = Guid.NewGuid(),
            AgentId = "agent-01",
            Type = AgentCommandType.StartCollection,
            ExpiresAtUtc = new DateTimeOffset(2026, 7, 11, 2, 0, 0, TimeSpan.Zero)
        };

        Assert.False(command.CanExecuteAt(new DateTimeOffset(2026, 7, 11, 2, 0, 1, TimeSpan.Zero)));
    }

    [Fact]
    public void Declarative_config_rejects_remote_script_code()
    {
        var package = new ConfigPackage
        {
            AssignmentId = Guid.NewGuid(),
            Version = 3,
            AgentId = "agent-01",
            PayloadJson = "{\"scriptCode\":\"System.Diagnostics.Process.Start('cmd')\"}",
            Sha256 = "invalid"
        };

        var result = package.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("脚本", StringComparison.Ordinal));
    }

    [Fact]
    public void Heartbeat_is_offline_after_thirty_seconds()
    {
        var heartbeatAt = new DateTimeOffset(2026, 7, 11, 1, 0, 0, TimeSpan.Zero);

        Assert.True(AgentPresence.IsOnline(heartbeatAt, heartbeatAt.AddSeconds(30)));
        Assert.False(AgentPresence.IsOnline(heartbeatAt, heartbeatAt.AddSeconds(31)));
    }

    [Fact]
    public void Request_identity_is_stable_for_agent_and_request_id()
    {
        var requestId = Guid.NewGuid();
        var first = RequestIdentity.Create("agent-01", requestId);
        var second = RequestIdentity.Create("agent-01", requestId);

        Assert.Equal(first, second);
        Assert.NotEqual(first, RequestIdentity.Create("agent-02", requestId));
    }
}
