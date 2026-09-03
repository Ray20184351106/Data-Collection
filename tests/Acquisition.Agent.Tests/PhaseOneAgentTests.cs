using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Acquisition.Agent;
using Acquisition.Agent.Services;
using Acquisition.Agent.Storage;
using Acquisition.Contracts;
using Xunit;

namespace Acquisition.Agent.Tests;

public sealed class PhaseOneAgentTests
{
    [Fact]
    public void Dpapi_identity_is_bound_to_the_agent_service_account()
    {
        if (!OperatingSystem.IsWindows()) return;
        AssertDpapiIdentityIsAccountBound();
    }

    [SupportedOSPlatform("windows")]
    private static void AssertDpapiIdentityIsAccountBound()
    {
        var plaintext = Encoding.UTF8.GetBytes("device-secret");
        var protector = new DpapiAgentSecretProtector();

        var protectedBytes = protector.Protect(plaintext);

        Assert.Equal(DataProtectionScope.CurrentUser, DpapiAgentSecretProtector.ProtectionScope);
        Assert.Equal(plaintext, protector.Unprotect(protectedBytes));
        Assert.NotEmpty(protectedBytes);
    }

    [Fact]
    public async Task Agent_enrolls_once_persists_protected_identity_and_uses_device_credential()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var options = new AgentOptions
        {
            AgentId = "LINE01-PC01",
            CenterBaseUrl = "http://center.test",
            RegistrationKey = "",
            IdentityPath = Path.Combine(root, "identity.bin"),
            EnrollmentTokenPath = Path.Combine(root, "enrollment.json")
        };
        await File.WriteAllTextAsync(options.EnrollmentTokenPath, JsonSerializer.Serialize(new AgentEnrollmentTokenFile
        {
            AgentId = options.AgentId,
            EnrollmentToken = "one-time-token"
        }));
        var handler = new EnrollmentHandler();
        var identity = new AgentIdentityStore(options, new TestProtector());
        var client = new CenterClient(new HttpClient(handler) { BaseAddress = new Uri(options.CenterBaseUrl) }, options, identity);

        await client.SendHeartbeatAsync(new AgentHeartbeat
        {
            AgentId = options.AgentId, RequestId = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        await client.SendHeartbeatAsync(new AgentHeartbeat
        {
            AgentId = options.AgentId, RequestId = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        Assert.Equal(1, handler.EnrollmentRequests);
        Assert.Equal(new[] { "device-secret", "device-secret" }, handler.HeartbeatCredentials);
        Assert.False(File.Exists(options.EnrollmentTokenPath));
        Assert.True(File.Exists(options.IdentityPath));
        Assert.DoesNotContain("device-secret", await File.ReadAllTextAsync(options.IdentityPath));

        File.Delete(options.IdentityPath);
        Directory.Delete(root);
    }

    [Fact]
    public async Task Config_file_transaction_restores_old_file_or_no_file_state()
    {
        var root = Path.Combine(Path.GetTempPath(), $"config-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var existing = Path.Combine(root, "existing.json");
        var first = Path.Combine(root, "first.json");
        await File.WriteAllTextAsync(existing, "old");

        await using (var transaction = await LegacyConfigFileTransaction.StageAsync(existing, "new", CancellationToken.None))
        {
            Assert.Equal("new", await File.ReadAllTextAsync(existing));
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await using (var transaction = await LegacyConfigFileTransaction.StageAsync(first, "new", CancellationToken.None))
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }

        Assert.Equal("old", await File.ReadAllTextAsync(existing));
        Assert.False(File.Exists(first));
        File.Delete(existing);
        Directory.Delete(root);
    }

    [Fact]
    public async Task Local_store_reports_real_health_and_effective_config_hash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-health-{Guid.NewGuid():N}.db");
        await using var store = new AgentLocalStore(path);
        await store.InitializeAsync(CancellationToken.None);
        var package = new ConfigPackage { Version = 3, PayloadJson = "{\"machines\":[]}" };
        package.Sha256 = ConfigPackage.ComputeSha256(package.PayloadJson);

        await store.SaveAppliedConfigAsync(package, CancellationToken.None);
        var state = await store.GetAppliedConfigStateAsync(CancellationToken.None);

        Assert.True(await store.CheckHealthAsync(CancellationToken.None));
        Assert.Equal(3, state.Version);
        Assert.Equal(package.Sha256, state.Sha256);
        File.Delete(path);
    }

    private sealed class EnrollmentHandler : HttpMessageHandler
    {
        public int EnrollmentRequests { get; private set; }
        public List<string> HeartbeatCredentials { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/enroll", StringComparison.Ordinal))
            {
                EnrollmentRequests++;
                var enrollment = await request.Content!.ReadFromJsonAsync<AgentEnrollmentRequest>(cancellationToken: cancellationToken);
                Assert.Equal("one-time-token", enrollment!.EnrollmentToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AgentEnrollmentResponse
                    {
                        AgentId = enrollment.AgentId, DeviceCredential = "device-secret", EnrolledAtUtc = DateTimeOffset.UtcNow
                    })
                };
            }
            HeartbeatCredentials.Add(request.Headers.GetValues("X-Registration-Key").Single());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }

    private sealed class TestProtector : IAgentSecretProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Reverse().ToArray();
        public byte[] Unprotect(byte[] protectedData) => protectedData.Reverse().ToArray();
    }
}
