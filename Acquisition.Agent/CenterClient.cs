using System.Net.Http.Json;
using Acquisition.Agent.Services;
using Acquisition.Contracts;

namespace Acquisition.Agent;

public sealed class CenterClient(HttpClient httpClient, AgentOptions options, AgentIdentityStore identityStore)
{
    private readonly SemaphoreSlim _identityGate = new(1, 1);
    private string? _credential;

    private async Task AddAgentIdentityAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("X-Agent-Id", options.AgentId);
        request.Headers.TryAddWithoutValidation("X-Registration-Key", await GetCredentialAsync(cancellationToken));
    }

    private async Task<string> GetCredentialAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_credential)) return _credential;
        await _identityGate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_credential)) return _credential;
            _credential = await identityStore.LoadCredentialAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(_credential)) return _credential;

            var enrollment = await identityStore.LoadEnrollmentTokenAsync(cancellationToken);
            if (enrollment is not null)
            {
                using var response = await httpClient.PostAsJsonAsync("api/agent/v1/enroll", new AgentEnrollmentRequest
                {
                    AgentId = enrollment.AgentId,
                    EnrollmentToken = enrollment.EnrollmentToken
                }, cancellationToken);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<AgentEnrollmentResponse>(cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException("中心未返回设备身份。");
                await identityStore.SaveIdentityAndConsumeEnrollmentAsync(result, cancellationToken);
                _credential = result.DeviceCredential;
                return _credential;
            }

            if (!string.IsNullOrWhiteSpace(options.RegistrationKey))
            {
                _credential = options.RegistrationKey;
                return _credential;
            }
            throw new InvalidOperationException("Agent既没有设备身份、一次性注册文件，也没有兼容注册码。");
        }
        finally { _identityGate.Release(); }
    }

    public async Task SendHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/v1/heartbeats") { Content = JsonContent.Create(heartbeat) };
        await AddAgentIdentityAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await identityStore.MarkHeartbeatSucceededAsync(cancellationToken);
    }

    public async Task<bool> SendRecordsAsync(CollectionRecordBatch batch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/v1/collection-records/batch") { Content = JsonContent.Create(batch) };
        await AddAgentIdentityAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AcceptedResponse>(cancellationToken: cancellationToken);
        return result?.Accepted is true;
    }

    public async Task<List<CommandEnvelope>> GetCommandsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/agent/v1/commands");
        await AddAgentIdentityAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CommandEnvelope>>(cancellationToken: cancellationToken) ?? new();
    }

    public async Task AcknowledgeCommandAsync(CommandAcknowledgement acknowledgement, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/agent/v1/commands/{acknowledgement.CommandId}/ack")
            { Content = JsonContent.Create(acknowledgement) };
        await AddAgentIdentityAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<ConfigPackage>> GetConfigsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/agent/v1/config-assignments");
        await AddAgentIdentityAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ConfigPackage>>(cancellationToken: cancellationToken) ?? new();
    }

    public async Task AcknowledgeConfigAsync(ConfigApplyResult result, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/agent/v1/config-assignments/{result.AssignmentId}/ack")
            { Content = JsonContent.Create(result) };
        await AddAgentIdentityAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed class AcceptedResponse { public bool Accepted { get; set; } }
}
