using Acquisition.Center.Data;
using Acquisition.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Acquisition.Center.Services;

public sealed class ConfigManagementService(CenterDbContext db)
{
    public async Task<ConfigVersionEntity> SyncInternalAsync(string name, string payloadJson, string minimumAgentVersion,
        IReadOnlyCollection<string> agentIds, string actor, CancellationToken cancellationToken)
    {
        var targets = agentIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (targets.Count == 0) throw new ArgumentException("至少选择一个Agent。", nameof(agentIds));
        var version = (await db.ConfigVersions.MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var hash = ConfigPackage.ComputeSha256(payloadJson);
        var validation = new ConfigPackage
        {
            AssignmentId = Guid.NewGuid(), AgentId = "internal-sync", Version = version,
            MinimumAgentVersion = minimumAgentVersion, PayloadJson = payloadJson, Sha256 = hash
        }.Validate();
        if (!validation.IsValid) throw new ArgumentException(string.Join("; ", validation.Errors), nameof(payloadJson));

        var entity = new ConfigVersionEntity
        {
            Id = Guid.NewGuid(), Version = version, Name = name.Trim(), PayloadJson = payloadJson,
            MinimumAgentVersion = minimumAgentVersion, Sha256 = hash, State = ConfigLifecycleState.Published,
            CreatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = actor
        };
        db.ConfigVersions.Add(entity);
        foreach (var agentId in targets)
        {
            db.ConfigAssignments.Add(new ConfigAssignmentEntity
            {
                Id = Guid.NewGuid(), ConfigVersionId = entity.Id, AgentId = agentId, CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        db.AuditLogs.Add(new AuditLogEntity
        {
            Actor = actor, Action = "config.sync", Target = entity.Id.ToString(),
            Detail = $"version={version};targets={targets.Count}", CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }
}
