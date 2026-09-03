using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Acquisition.Center.Bootstrap;
using Acquisition.Center.Configuration;
using Acquisition.Center.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Acquisition.Center.Tests;

[CollectionDefinition(nameof(BootstrapEnvironmentCollection), DisableParallelization = true)]
public sealed class BootstrapEnvironmentCollection;

[Collection(nameof(BootstrapEnvironmentCollection))]
public sealed class BootstrapConfigurationTests
{
    [Fact]
    public void Default_settings_path_honors_environment_override()
    {
        var expected = Path.Combine(Path.GetTempPath(), $"center-settings-{Guid.NewGuid():N}.json");
        var previous = Environment.GetEnvironmentVariable(CenterBootstrapStore.SettingsPathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(CenterBootstrapStore.SettingsPathEnvironmentVariable, expected);

            var actual = CenterBootstrapStore.ResolveSettingsPath();

            Assert.Equal(Path.GetFullPath(expected), actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CenterBootstrapStore.SettingsPathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Isolated_test_allows_localdb_and_keeps_bind_and_advertised_addresses_separate()
    {
        var draft = CreateDraft(CenterRunMode.IsolatedTest);
        draft.BindAddress = "0.0.0.0";
        draft.AdvertisedBaseUrl = "http://192.168.20.109:5080";

        var validation = CenterSetupValidator.Validate(draft);

        Assert.True(validation.IsValid, string.Join("; ", validation.Issues.Select(x => x.Message)));
    }

    [Fact]
    public void Production_rejects_localdb()
    {
        var draft = CreateDraft(CenterRunMode.Production);
        draft.AdvertisedBaseUrl = "http://192.168.20.109:5080";

        var validation = CenterSetupValidator.Validate(draft);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue => issue.Code == "PRODUCTION_LOCALDB_NOT_ALLOWED");
    }

    [Fact]
    public async Task Create_generates_secrets_but_persists_only_the_admin_code_hash()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var store = new CenterBootstrapStore(settingsPath);

            var result = await store.CreateAsync(CreateDraft(CenterRunMode.IsolatedTest));
            var persistedJson = await File.ReadAllTextAsync(settingsPath);
            var loaded = await store.LoadAsync();
            var creationResponseJson = JsonSerializer.Serialize(result);

            Assert.NotNull(loaded);
            Assert.NotEmpty(result.DeploymentAdminAccessCode);
            Assert.DoesNotContain(result.DeploymentAdminAccessCode, persistedJson, StringComparison.Ordinal);
            Assert.NotEmpty(loaded!.DeploymentAdminAccessCodeHash);
            Assert.NotEmpty(loaded.DeploymentAdminAccessCodeSalt);
            Assert.NotEmpty(loaded.CenterSharedCompatibilityKey);
            Assert.True(CenterBootstrapStore.VerifyDeploymentAdminAccessCode(loaded, result.DeploymentAdminAccessCode));
            Assert.False(CenterBootstrapStore.VerifyDeploymentAdminAccessCode(loaded, "incorrect-access-code"));
            Assert.Contains("\"runMode\": \"IsolatedTest\"", persistedJson, StringComparison.Ordinal);
            Assert.DoesNotContain(loaded.CenterSharedCompatibilityKey, creationResponseJson, StringComparison.Ordinal);
            Assert.DoesNotContain(loaded.DatabaseConnectionString, creationResponseJson, StringComparison.Ordinal);
            Assert.DoesNotContain(loaded.DeploymentAdminAccessCodeHash, creationResponseJson, StringComparison.Ordinal);
        }
        finally
        {
            CleanupSettingsPath(settingsPath);
        }
    }

    [Fact]
    public async Task Persisted_bootstrap_secrets_are_not_readable_through_inherited_windows_acl()
    {
        if (!OperatingSystem.IsWindows()) return;
        var settingsPath = CreateSettingsPath();
        try
        {
            var store = new CenterBootstrapStore(settingsPath);

            await store.CreateAsync(CreateDraft(CenterRunMode.IsolatedTest));

            AssertBootstrapAclIsRestricted(settingsPath);
        }
        finally
        {
            CleanupSettingsPath(settingsPath);
        }
    }

    [Fact]
    public async Task Concurrent_create_allows_only_one_access_code_to_become_valid()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var firstStore = new CenterBootstrapStore(settingsPath);
            var secondStore = new CenterBootstrapStore(settingsPath);
            async Task<(CenterBootstrapCreationResult? Result, Exception? Error)> TryCreateAsync(CenterBootstrapStore store)
            {
                try { return (await store.CreateAsync(CreateDraft(CenterRunMode.IsolatedTest)), null); }
                catch (Exception ex) { return (null, ex); }
            }

            var attempts = await Task.WhenAll(TryCreateAsync(firstStore), TryCreateAsync(secondStore));
            var success = Assert.Single(attempts, x => x.Result is not null);
            var failure = Assert.Single(attempts, x => x.Error is not null);
            var loaded = await firstStore.LoadAsync();

            Assert.IsType<InvalidOperationException>(failure.Error);
            Assert.True(CenterBootstrapStore.VerifyDeploymentAdminAccessCode(loaded!, success.Result!.DeploymentAdminAccessCode));
        }
        finally
        {
            CleanupSettingsPath(settingsPath);
        }
    }

    [Fact]
    public async Task Corrupt_hash_metadata_is_rejected_without_throwing()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var store = new CenterBootstrapStore(settingsPath);
            var created = await store.CreateAsync(CreateDraft(CenterRunMode.IsolatedTest));
            var settings = Assert.IsType<CenterBootstrapOptions>(await store.LoadAsync());
            settings!.DeploymentAdminAccessCodeHash = "";

            var accepted = CenterBootstrapStore.VerifyDeploymentAdminAccessCode(
                settings, created.DeploymentAdminAccessCode);

            Assert.False(accepted);
        }
        finally
        {
            CleanupSettingsPath(settingsPath);
        }
    }

    [Fact]
    public async Task Save_replaces_the_complete_json_document_without_leaving_temporary_files()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var store = new CenterBootstrapStore(settingsPath);
            await store.CreateAsync(CreateDraft(CenterRunMode.IsolatedTest));
            var settings = await store.LoadAsync();
            settings!.AdvertisedBaseUrl = "http://192.168.20.110:5080";

            await store.SaveAsync(settings);

            var loaded = await store.LoadAsync();
            Assert.Equal("http://192.168.20.110:5080", loaded!.AdvertisedBaseUrl);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp"));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }
        finally
        {
            CleanupSettingsPath(settingsPath);
        }
    }

    [Fact]
    public async Task Effective_configuration_applies_overrides_without_exposing_passwords_or_keys()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var store = new CenterBootstrapStore(settingsPath);
            await store.CreateAsync(CreateDraft(CenterRunMode.IsolatedTest));
            var settings = Assert.IsType<CenterBootstrapOptions>(await store.LoadAsync());
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Center:BindAddress"] = "10.0.0.5",
                ["ConnectionStrings:CenterDatabase"] = "Server=sql01;Database=AcquisitionCenter;User ID=center-user;Password=do-not-return;Encrypt=true",
                ["InternalLan:RegistrationKey"] = "do-not-return-shared-key"
            }).Build();
            var service = new EffectiveConfigurationService(configuration);

            var snapshot = service.Build(settings);
            var json = JsonSerializer.Serialize(snapshot);

            Assert.Equal("IsolatedTest", snapshot.RunMode);
            Assert.Equal("10.0.0.5", snapshot.BindAddress);
            Assert.Equal("sql01", snapshot.Database.Server);
            Assert.Equal("AcquisitionCenter", snapshot.Database.Database);
            Assert.Equal("SqlServerLogin", snapshot.Database.Authentication);
            Assert.True(snapshot.DeploymentAdminAccessCode.IsConfigured);
            Assert.True(snapshot.CenterSharedCompatibilityKey.IsConfigured);
            Assert.DoesNotContain("do-not-return", json, StringComparison.Ordinal);
            Assert.DoesNotContain(settings.CenterSharedCompatibilityKey, json, StringComparison.Ordinal);
            Assert.DoesNotContain(settings.DeploymentAdminAccessCodeHash, json, StringComparison.Ordinal);
            Assert.DoesNotContain(settings.DeploymentAdminAccessCodeSalt, json, StringComparison.Ordinal);
        }
        finally
        {
            CleanupSettingsPath(settingsPath);
        }
    }

    [Fact]
    public async Task Deployment_admin_authentication_creates_an_operator_principal_only_for_the_one_time_code()
    {
        var settingsPath = CreateSettingsPath();
        try
        {
            var store = new CenterBootstrapStore(settingsPath);
            var created = await store.CreateAsync(CreateDraft(CenterRunMode.IsolatedTest));
            var authentication = new DeploymentOperatorAuthenticationService(store);

            var accepted = await authentication.AuthenticateAsync(created.DeploymentAdminAccessCode);
            var rejected = await authentication.AuthenticateAsync("incorrect-access-code");

            Assert.NotNull(accepted);
            Assert.Equal("deployment-operator", accepted!.Identity?.Name);
            Assert.Contains(accepted.Claims, claim => claim.Type == "kind" && claim.Value == "operator");
            Assert.Null(rejected);
        }
        finally
        {
            CleanupSettingsPath(settingsPath);
        }
    }

    private static CenterBootstrapDraft CreateDraft(CenterRunMode runMode) => new()
    {
        RunMode = runMode,
        BindAddress = "127.0.0.1",
        Port = 5080,
        AdvertisedBaseUrl = "http://127.0.0.1:5080",
        DatabaseConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=AcquisitionCenter;Integrated Security=true;TrustServerCertificate=true",
        InternalLanEnabled = true,
        RequireHttps = false
    };

    [SupportedOSPlatform("windows")]
    private static void AssertBootstrapAclIsRestricted(string settingsPath)
    {
        var security = FileSystemAclExtensions.GetAccessControl(new FileInfo(settingsPath));
        Assert.True(security.AreAccessRulesProtected);
        var broadReaderSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            new SecurityIdentifier(WellKnownSidType.WorldSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value
        };
        Assert.DoesNotContain(
            security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .OfType<FileSystemAccessRule>(),
            rule => rule.AccessControlType == AccessControlType.Allow
                && broadReaderSids.Contains(((SecurityIdentifier)rule.IdentityReference).Value)
                && (rule.FileSystemRights & FileSystemRights.ReadData) != 0);
    }

    private static string CreateSettingsPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AcquisitionCenterBootstrapTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "center-bootstrap.json");
    }

    private static void CleanupSettingsPath(string settingsPath)
    {
        if (File.Exists(settingsPath)) File.Delete(settingsPath);
        var directory = Path.GetDirectoryName(settingsPath)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
    }
}
