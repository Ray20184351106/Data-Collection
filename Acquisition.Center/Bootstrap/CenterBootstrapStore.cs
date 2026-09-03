using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acquisition.Center.Bootstrap;

public sealed class CenterBootstrapStore
{
    public const string SettingsPathEnvironmentVariable = "ACQUISITION_CENTER_SETTINGS_PATH";
    private const int Pbkdf2Iterations = 600_000;
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeGate;

    public CenterBootstrapStore(string? settingsPath = null)
    {
        SettingsPath = Path.GetFullPath(string.IsNullOrWhiteSpace(settingsPath) ? ResolveSettingsPath() : settingsPath);
        _writeGate = WriteGates.GetOrAdd(SettingsPath, _ => new SemaphoreSlim(1, 1));
    }

    public string SettingsPath { get; }

    public static string ResolveSettingsPath()
    {
        var configured = Environment.GetEnvironmentVariable(SettingsPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AcquisitionCenter",
            "center-bootstrap.json");
    }

    public async Task<CenterBootstrapCreationResult> CreateAsync(
        CenterBootstrapDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var validation = CenterSetupValidator.Validate(draft);
        if (!validation.IsValid)
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(x => x.Message)), nameof(draft));
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(SettingsPath))
                throw new InvalidOperationException("Center首次启动配置已经存在，不能重新生成部署管理员访问码。");

            var deploymentAdminAccessCode = GenerateSecret(18);
            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            var hash = HashAccessCode(deploymentAdminAccessCode, salt, Pbkdf2Iterations);
            var now = DateTimeOffset.UtcNow;
            var settings = new CenterBootstrapOptions
            {
                RunMode = draft.RunMode,
                BindAddress = draft.BindAddress.Trim(),
                Port = draft.Port,
                AdvertisedBaseUrl = draft.AdvertisedBaseUrl.TrimEnd('/'),
                DatabaseConnectionString = draft.DatabaseConnectionString,
                InternalLanEnabled = draft.InternalLanEnabled,
                RequireHttps = draft.RequireHttps,
                DeploymentAdminAccessCodeIterations = Pbkdf2Iterations,
                DeploymentAdminAccessCodeSalt = Convert.ToBase64String(salt),
                DeploymentAdminAccessCodeHash = Convert.ToBase64String(hash),
                CenterSharedCompatibilityKey = GenerateSecret(32),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            await WriteAtomicAsync(settings, cancellationToken);
            return new CenterBootstrapCreationResult(deploymentAdminAccessCode);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<CenterBootstrapOptions?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) return null;
        await using var stream = new FileStream(
            SettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<CenterBootstrapOptions>(stream, SerializerOptions, cancellationToken);
    }

    public async Task SaveAsync(CenterBootstrapOptions settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await WriteAtomicAsync(settings, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public static bool VerifyDeploymentAdminAccessCode(CenterBootstrapOptions settings, string? suppliedAccessCode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(suppliedAccessCode) ||
            settings.DeploymentAdminAccessCodeIterations is < 100_000 or > 5_000_000 ||
            string.IsNullOrWhiteSpace(settings.DeploymentAdminAccessCodeSalt) ||
            string.IsNullOrWhiteSpace(settings.DeploymentAdminAccessCodeHash)) return false;
        try
        {
            var salt = Convert.FromBase64String(settings.DeploymentAdminAccessCodeSalt);
            var expected = Convert.FromBase64String(settings.DeploymentAdminAccessCodeHash);
            if (salt.Length is < 8 or > 64 || expected.Length is < 16 or > 64) return false;
            var actual = HashAccessCode(suppliedAccessCode, salt, settings.DeploymentAdminAccessCodeIterations, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    private async Task WriteAtomicAsync(CenterBootstrapOptions settings, CancellationToken cancellationToken)
    {
        ValidatePersistedSettings(settings);
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("Center配置路径缺少父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            ProtectSensitiveFile(temporaryPath);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ProtectSensitiveFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return;
        }

        ProtectSensitiveWindowsFile(path);
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectSensitiveWindowsFile(string path)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("无法识别Center配置文件所有者。");
        var security = new FileSecurity();
        security.SetOwner(currentUser);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[]
                 {
                     currentUser,
                     new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                 }.DistinctBy(value => value.Value, StringComparer.OrdinalIgnoreCase))
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid, FileSystemRights.FullControl, AccessControlType.Allow));
        }
        FileSystemAclExtensions.SetAccessControl(new FileInfo(path), security);
    }

    private static void ValidatePersistedSettings(CenterBootstrapOptions settings)
    {
        var validation = CenterSetupValidator.Validate(settings.ToDraft());
        if (!validation.IsValid)
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(x => x.Message)), nameof(settings));
        if (settings.DeploymentAdminAccessCodeIterations < 100_000 ||
            string.IsNullOrWhiteSpace(settings.DeploymentAdminAccessCodeSalt) ||
            string.IsNullOrWhiteSpace(settings.DeploymentAdminAccessCodeHash) ||
            string.IsNullOrWhiteSpace(settings.CenterSharedCompatibilityKey))
            throw new ArgumentException("Center安全配置不完整。", nameof(settings));
    }

    private static byte[] HashAccessCode(string accessCode, byte[] salt, int iterations, int outputLength = HashLength) =>
        Rfc2898DeriveBytes.Pbkdf2(accessCode, salt, iterations, HashAlgorithmName.SHA256, outputLength);

    private static string GenerateSecret(int byteLength)
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength));
        return value.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
