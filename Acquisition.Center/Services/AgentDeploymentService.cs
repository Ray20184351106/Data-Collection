using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Acquisition.Center.Data;
using Acquisition.Center.Security;
using Acquisition.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Acquisition.Center.Services;

public sealed class DeploymentPackageOptions
{
    public string AdvertisedBaseUrl { get; set; } = "";
    public string SetupExecutablePath { get; set; } = "";
    public string AgentPayloadPath { get; set; } = "";
    public string AgentVersion { get; set; } = "1.0.0";
}

public sealed record AgentDeploymentPackage(Guid DeploymentId, string AgentId, string FileName, byte[] Content);

public sealed partial class AgentDeploymentService
{
    private const long MaximumPackageBytes = 1024L * 1024 * 1024;
    private readonly CenterDbContext _db;
    private readonly DeploymentPackageOptions _options;
    private readonly TimeProvider _timeProvider;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public AgentDeploymentService(CenterDbContext db, DeploymentPackageOptions options)
        : this(db, options, TimeProvider.System) { }

    public AgentDeploymentService(CenterDbContext db, DeploymentPackageOptions options, TimeProvider timeProvider)
    {
        _db = db;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<AgentDeploymentPackage> CreatePackageAsync(
        CreateAgentDeploymentRequest input, string actor, CancellationToken cancellationToken)
    {
        var agentId = NormalizeAgentId(input.AgentId);
        var acquisitionDirectory = NormalizeLocalDirectory(input.AcquisitionAppDirectory);
        var baseUrl = NormalizeCenterBaseUrl(_options.AdvertisedBaseUrl);
        var setupPath = Path.GetFullPath(_options.SetupExecutablePath);
        var payloadRoot = Path.GetFullPath(_options.AgentPayloadPath);
        if (!File.Exists(setupPath)) throw new InvalidOperationException("AgentSetup.exe发布资产不存在。");
        if (!Directory.Exists(payloadRoot)) throw new InvalidOperationException("Agent自包含发布资产不存在。");
        RejectReparsePoint(setupPath);
        RejectReparsePoint(payloadRoot);

        var now = _timeProvider.GetUtcNow();
        var token = DeviceCredentialHasher.CreateSecret();
        var entity = await _db.AgentDeployments.SingleOrDefaultAsync(x => x.AgentId == agentId, cancellationToken);
        if (entity is not null && entity.EnrolledAtUtc.HasValue)
            throw new InvalidOperationException("该Agent已经注册，不能重新签发初始安装包。");

        entity ??= new AgentDeploymentEntity { Id = Guid.NewGuid(), AgentId = agentId };
        if (_db.Entry(entity).State == EntityState.Detached) _db.AgentDeployments.Add(entity);
        entity.Site = Trim(input.Site, 100);
        entity.Building = Trim(input.Building, 100);
        entity.Line = Trim(input.Line, 100);
        entity.AcquisitionAppDirectory = acquisitionDirectory;
        entity.StartWinFormsOnLogon = input.StartWinFormsOnLogon;
        entity.State = AgentDeploymentLifecycleState.PackageIssued;
        entity.EnrollmentTokenHash = DeviceCredentialHasher.Hash(token);
        entity.EnrollmentExpiresAtUtc = now.AddHours(24);
        entity.EnrollmentConsumedAtUtc = null;
        entity.DeviceCredentialHash = null;
        entity.PackageIssuedAtUtc = now;
        entity.CreatedBy = actor;

        var manifest = new AgentDeploymentManifest
        {
            DeploymentId = entity.Id,
            AgentId = entity.AgentId,
            CenterBaseUrl = baseUrl,
            EnrollmentToken = token,
            EnrollmentExpiresAtUtc = entity.EnrollmentExpiresAtUtc,
            Site = entity.Site,
            Building = entity.Building,
            Line = entity.Line,
            AcquisitionAppDirectory = acquisitionDirectory,
            LegacyDatabasePath = Path.Combine(acquisitionDirectory, "Data", "采集记录.db"),
            LegacyConfigPath = Path.Combine(acquisitionDirectory, "config.json"),
            LegacyExecutablePath = ResolveLegacyExecutablePath(acquisitionDirectory),
            AgentVersion = Trim(_options.AgentVersion, 50),
            StartWinFormsOnLogon = entity.StartWinFormsOnLogon
        };

        var package = await BuildPackageAsync(setupPath, payloadRoot, manifest, cancellationToken);
        _db.AuditLogs.Add(new AuditLogEntity
        {
            Actor = actor,
            Action = "deployment.package.issue",
            Target = entity.AgentId,
            Detail = $"deployment={entity.Id};expires={entity.EnrollmentExpiresAtUtc:O}",
            CreatedAtUtc = now
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new AgentDeploymentPackage(entity.Id, entity.AgentId, $"{entity.AgentId}-AgentSetup.zip", package);
    }

    public async Task<List<AgentDeploymentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return await _db.AgentDeployments.AsNoTracking().OrderBy(x => x.AgentId).Select(x => new AgentDeploymentSummary
        {
            Id = x.Id,
            AgentId = x.AgentId,
            Site = x.Site,
            Building = x.Building,
            Line = x.Line,
            AcquisitionAppDirectory = x.AcquisitionAppDirectory,
            StartWinFormsOnLogon = x.StartWinFormsOnLogon,
            State = x.LastHeartbeatAtUtc.HasValue && x.LastHeartbeatAtUtc >= now.AddSeconds(-30)
                ? AgentDeploymentLifecycleState.Online
                : x.EnrolledAtUtc.HasValue ? AgentDeploymentLifecycleState.Offline : x.State,
            PackageIssuedAtUtc = x.PackageIssuedAtUtc,
            EnrolledAtUtc = x.EnrolledAtUtc,
            FirstHeartbeatAtUtc = x.FirstHeartbeatAtUtc,
            LastHeartbeatAtUtc = x.LastHeartbeatAtUtc
        }).ToListAsync(cancellationToken);
    }

    private static async Task<byte[]> BuildPackageAsync(
        string setupPath, string payloadRoot, AgentDeploymentManifest deployment, CancellationToken cancellationToken)
    {
        var sourceFiles = new List<(string Source, string Entry)>
        {
            (setupPath, "AgentSetup.exe")
        };
        foreach (var source in EnumeratePayloadFiles(payloadRoot, cancellationToken))
        {
            var relative = Path.GetRelativePath(payloadRoot, source);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative))
                throw new InvalidOperationException("Agent发布资产路径越界。");
            if (Path.GetFileName(relative).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)) continue;
            sourceFiles.Add((source, "AgentPayload/" + relative.Replace('\\', '/')));
        }
        if (sourceFiles.Count == 1) throw new InvalidOperationException("Agent自包含发布目录为空。");
        if (sourceFiles.Sum(file => new FileInfo(file.Source).Length) > MaximumPackageBytes)
            throw new InvalidOperationException("Agent安装包超过一期允许的1GB上限。");

        var payloadManifest = new AgentPayloadManifest();
        foreach (var file in sourceFiles)
        {
            await using var stream = File.OpenRead(file.Source);
            payloadManifest.Files.Add(new AgentPayloadFile
            {
                Path = file.Entry,
                Length = stream.Length,
                Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant()
            });
        }

        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteJsonEntryAsync(archive, "deployment.json", deployment, cancellationToken);
            await WriteJsonEntryAsync(archive, "payload.manifest.json", payloadManifest, cancellationToken);
            foreach (var file in sourceFiles)
            {
                var entry = archive.CreateEntry(file.Entry, CompressionLevel.Optimal);
                await using var target = entry.Open();
                await using var source = File.OpenRead(file.Source);
                await source.CopyToAsync(target, cancellationToken);
            }
        }
        return output.ToArray();
    }

    private static IEnumerable<string> EnumeratePayloadFiles(string payloadRoot, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(payloadRoot);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparsePoint(directory);
                pending.Push(directory);
            }
            foreach (var file in Directory.EnumerateFiles(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparsePoint(file);
                yield return file;
            }
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Agent发布资产不能包含重解析点。");
    }

    private static async Task WriteJsonEntryAsync<T>(ZipArchive archive, string name, T value, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static string NormalizeAgentId(string? value)
    {
        var normalized = (value ?? "").Trim().ToUpperInvariant();
        if (!AgentIdPattern().IsMatch(normalized))
            throw new ArgumentException("AgentId必须为3到64位，只能包含字母、数字、点、下划线和短横线。", nameof(value));
        return normalized;
    }

    private static string NormalizeLocalDirectory(string? value)
    {
        var path = (value ?? "").Trim();
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("采集程序目录必须是目标电脑上的本地绝对路径，不能使用UNC路径。", nameof(value));
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeCenterBaseUrl(string? value)
    {
        if (!Uri.TryCreate((value ?? "").Trim().TrimEnd('/'), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Host is "0.0.0.0" or "::")
            throw new InvalidOperationException("Deployment:AdvertisedBaseUrl必须是Agent可访问的HTTP或HTTPS地址，不能使用0.0.0.0。");
        return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/');
    }

    private static string ResolveLegacyExecutablePath(string acquisitionDirectory)
    {
        var localized = Path.Combine(acquisitionDirectory, "文件数据采集系统.exe");
        var projectNamed = Path.Combine(acquisitionDirectory, "MachineDataAcquisitionSystem.exe");
        return File.Exists(localized) ? localized : projectNamed;
    }

    private static string Trim(string? value, int maximumLength)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentIdPattern();
}
