using System.Security.Cryptography;
using Acquisition.Agent.Setup;

namespace Acquisition.Agent.Setup.Tests;

public sealed class PayloadIntegrityVerifierTests
{
    [Fact]
    public void Matching_payload_hash_and_length_are_accepted()
    {
        var root = CreatePackageRoot();
        var payloadDirectory = Path.Combine(root, "AgentPayload");
        var setup = Path.Combine(root, "AgentSetup.exe");
        var file = Path.Combine(payloadDirectory, "Acquisition.Agent.exe");
        Directory.CreateDirectory(payloadDirectory);
        File.WriteAllText(setup, "known setup");
        File.WriteAllText(file, "known payload");
        try
        {
            var setupBytes = File.ReadAllBytes(setup);
            var bytes = File.ReadAllBytes(file);
            var manifest = new PayloadManifest
            {
                SchemaVersion = 1,
                Files =
                [
                    new PayloadFileEntry("AgentSetup.exe", Convert.ToHexString(SHA256.HashData(setupBytes)).ToLowerInvariant(), setupBytes.LongLength),
                    new PayloadFileEntry("AgentPayload/Acquisition.Agent.exe", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength)
                ]
            };

            var result = new PayloadIntegrityVerifier().Verify(root, manifest);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
        }
        finally
        {
            File.Delete(setup);
            File.Delete(file);
            Directory.Delete(payloadDirectory);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void Payload_appsettings_is_rejected_because_setup_must_generate_effective_runtime_configuration()
    {
        var root = CreatePackageRoot();
        var payloadDirectory = Path.Combine(root, "AgentPayload");
        var file = Path.Combine(payloadDirectory, "appsettings.json");
        Directory.CreateDirectory(payloadDirectory);
        File.WriteAllText(file, "{}");
        try
        {
            var bytes = File.ReadAllBytes(file);
            var manifest = new PayloadManifest
            {
                SchemaVersion = 1,
                Files =
                [
                    new PayloadFileEntry(
                        "AgentPayload/appsettings.json",
                        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                        bytes.LongLength)
                ]
            };

            var result = new PayloadIntegrityVerifier().Verify(root, manifest);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("appsettings", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(payloadDirectory);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        var root = CreatePackageRoot();
        var payloadDirectory = Path.Combine(root, "AgentPayload");
        var file = Path.Combine(payloadDirectory, "Acquisition.Agent.exe");
        Directory.CreateDirectory(payloadDirectory);
        File.WriteAllText(file, "tampered");
        try
        {
            var manifest = new PayloadManifest
            {
                SchemaVersion = 1,
                Files = [new PayloadFileEntry("AgentPayload/Acquisition.Agent.exe", new string('0', 64), new FileInfo(file).Length)]
            };

            var result = new PayloadIntegrityVerifier().Verify(root, manifest);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("SHA-256", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(payloadDirectory);
            Directory.Delete(root);
        }
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("AgentPayload/../../outside.exe")]
    [InlineData("C:/Windows/System32/cmd.exe")]
    public void Traversal_or_absolute_payload_path_is_rejected(string relativePath)
    {
        var root = CreatePackageRoot();
        try
        {
            var manifest = new PayloadManifest
            {
                SchemaVersion = 1,
                Files = [new PayloadFileEntry(relativePath, new string('0', 64), 1)]
            };

            var result = new PayloadIntegrityVerifier().Verify(root, manifest);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("路径", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    private static string CreatePackageRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "agent-setup-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
