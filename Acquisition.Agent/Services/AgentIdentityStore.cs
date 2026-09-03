using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text.Json;
using Acquisition.Contracts;

namespace Acquisition.Agent.Services;

public sealed class AgentEnrollmentTokenFile
{
    public string AgentId { get; set; } = "";
    public string EnrollmentToken { get; set; } = "";
}

public interface IAgentSecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}

public sealed class DpapiAgentSecretProtector : IAgentSecretProtector
{
    private static readonly byte[] Entropy = "Acquisition.Agent.Identity.v1"u8.ToArray();
    [SupportedOSPlatform("windows")]
    public static DataProtectionScope ProtectionScope => DataProtectionScope.CurrentUser;

    public byte[] Protect(byte[] plaintext)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("设备身份DPAPI仅支持Windows。");
        return ProtectedData.Protect(plaintext, Entropy, ProtectionScope);
    }
    public byte[] Unprotect(byte[] protectedData)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("设备身份DPAPI仅支持Windows。");
        return ProtectedData.Unprotect(protectedData, Entropy, ProtectionScope);
    }
}

public sealed class AgentIdentityStore(AgentOptions options, IAgentSecretProtector protector)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<string?> LoadCredentialAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.IdentityPath)) return null;
        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(options.IdentityPath, cancellationToken);
            var identity = JsonSerializer.Deserialize<StoredAgentIdentity>(protector.Unprotect(protectedBytes), JsonOptions);
            return identity is not null && string.Equals(identity.AgentId, options.AgentId, StringComparison.OrdinalIgnoreCase)
                ? identity.DeviceCredential : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException)
        {
            throw new InvalidOperationException("设备身份文件无法读取或已损坏。", exception);
        }
    }

    public async Task<AgentEnrollmentTokenFile?> LoadEnrollmentTokenAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.EnrollmentTokenPath)) return null;
        try
        {
            await using var stream = File.OpenRead(options.EnrollmentTokenPath);
            var enrollment = await JsonSerializer.DeserializeAsync<AgentEnrollmentTokenFile>(stream, JsonOptions, cancellationToken);
            if (enrollment is null || string.IsNullOrWhiteSpace(enrollment.AgentId) || string.IsNullOrWhiteSpace(enrollment.EnrollmentToken))
                throw new InvalidOperationException("一次性注册文件内容不完整。");
            if (!string.Equals(enrollment.AgentId, options.AgentId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("一次性注册文件中的AgentId与运行配置不一致。");
            return enrollment;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("一次性注册文件格式无效。", exception);
        }
    }

    public async Task SaveIdentityAndConsumeEnrollmentAsync(
        AgentEnrollmentResponse enrollment, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(enrollment.AgentId, options.AgentId, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(enrollment.DeviceCredential))
                throw new InvalidOperationException("中心返回的设备身份无效。");
            var fullPath = Path.GetFullPath(options.IdentityPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(new StoredAgentIdentity
            {
                AgentId = options.AgentId,
                DeviceCredential = enrollment.DeviceCredential,
                EnrolledAtUtc = enrollment.EnrolledAtUtc
            }, JsonOptions);
            var temporary = fullPath + ".pending-" + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(temporary, protector.Protect(plaintext), cancellationToken);
            File.Move(temporary, fullPath, overwrite: true);
            if (File.Exists(options.EnrollmentTokenPath)) File.Delete(options.EnrollmentTokenPath);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkHeartbeatSucceededAsync(CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(options.InstallStatusPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(new
        {
            agentId = options.AgentId,
            firstHeartbeatSucceededAtUtc = DateTimeOffset.UtcNow
        }, JsonOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken);
    }

    private sealed class StoredAgentIdentity
    {
        public string AgentId { get; set; } = "";
        public string DeviceCredential { get; set; } = "";
        public DateTimeOffset EnrolledAtUtc { get; set; }
    }
}
