using System.Net.Http.Json;
using Acquisition.Contracts;

namespace Acquisition.Agent;

public sealed class CenterClient(HttpClient httpClient, AgentOptions options)
{
    private void AddAgentIdentity(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("X-Agent-Id", options.AgentId);
        request.Headers.TryAddWithoutValidation("X-Registration-Key", options.RegistrationKey);
    }

    public async Task SendHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/v1/heartbeats") { Content = JsonContent.Create(heartbeat) };
        AddAgentIdentity(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> SendRecordsAsync(CollectionRecordBatch batch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/v1/collection-records/batch") { Content = JsonContent.Create(batch) };
        AddAgentIdentity(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AcceptedResponse>(cancellationToken: cancellationToken);
        return result?.Accepted is true;
    }

    public async Task<List<CommandEnvelope>> GetCommandsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/agent/v1/commands"); AddAgentIdentity(request);
        using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CommandEnvelope>>(cancellationToken: cancellationToken) ?? new();
    }

    public async Task AcknowledgeCommandAsync(CommandAcknowledgement acknowledgement, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/agent/v1/commands/{acknowledgement.CommandId}/ack") { Content = JsonContent.Create(acknowledgement) };
        AddAgentIdentity(request); using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
    }

    public async Task<List<ConfigPackage>> GetConfigsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/agent/v1/config-assignments"); AddAgentIdentity(request);
        using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ConfigPackage>>(cancellationToken: cancellationToken) ?? new();
    }

    public async Task AcknowledgeConfigAsync(ConfigApplyResult result, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/agent/v1/config-assignments/{result.AssignmentId}/ack") { Content = JsonContent.Create(result) };
        AddAgentIdentity(request); using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
    }

    private sealed class AcceptedResponse { public bool Accepted { get; set; } }
}
