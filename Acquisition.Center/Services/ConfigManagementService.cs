using System.Data;
using Acquisition.Center.Data;
using Acquisition.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Acquisition.Center.Services;

public sealed class ConfigManagementService(CenterDbContext db)
{
    public async Task<ConfigPreviewResponse> PreviewAsync(ConfigPreviewRequest input, CancellationToken cancellationToken)
    {
        var document = MachineConfigurationDocument.Create(input.Machines);
        if (!document.IsValid) throw new ArgumentException(string.Join("; ", document.Errors), nameof(input.Machines));
        var targets = NormalizeTargets(input.AgentIds);
        if (targets.Count == 0) throw new ArgumentException("至少选择一个Agent。", nameof(input.AgentIds));
        ValidateNameAndVersion(input.Name, input.MinimumAgentVersion);

        var agents = (await db.Agents.AsNoTracking().Where(x => targets.Contains(x.Id)).ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var deployed = (await db.AgentDeployments.AsNoTracking().Where(x => targets.Contains(x.AgentId))
            .Select(x => x.AgentId).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var latestAssignments = await db.ConfigAssignments.AsNoTracking()
            .Where(x => targets.Contains(x.AgentId) && x.EffectiveVersion != null)
            .OrderByDescending(x => x.EffectiveObservedAtUtc)
            .Select(x => new { x.AgentId, x.EffectiveVersion, x.EffectiveSha256 })
            .ToListAsync(cancellationToken);

        var response = new ConfigPreviewResponse
        {
            PayloadJson = document.PayloadJson,
            PayloadSha256 = document.PayloadSha256,
            PreviewSha256 = ComputePreviewSha256(input.Name, input.MinimumAgentVersion, targets, document.PayloadSha256)
        };
        var now = DateTimeOffset.UtcNow;
        foreach (var agentId in targets)
        {
            agents.TryGetValue(agentId, out var agent);
            var effective = latestAssignments.FirstOrDefault(x => string.Equals(x.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
            response.Targets.Add(new ConfigTargetPreview
            {
                AgentId = agentId,
                Exists = agent is not null || deployed.Contains(agentId),
                IsOnline = agent is not null && AgentPresence.IsOnline(agent.LastHeartbeatUtc, now),
                EffectiveVersion = effective?.EffectiveVersion,
                EffectiveSha256 = effective?.EffectiveSha256,
                HasChanges = !string.Equals(effective?.EffectiveSha256, document.PayloadSha256, StringComparison.OrdinalIgnoreCase)
            });
        }
        if (response.Targets.Any(x => !x.Exists)) response.Warnings.Add("部分目标Agent尚未注册或创建设备部署记录。");
        if (response.Targets.Any(x => !x.IsOnline)) response.Warnings.Add("离线Agent会在恢复连接后领取配置任务。");
        return response;
    }

    public async Task<ConfigVersionEntity> PublishAsync(
        PublishConfigVersionRequest input, string actor, CancellationToken cancellationToken)
    {
        if (input.RequestId == Guid.Empty) throw new ArgumentException("RequestId不能为空。", nameof(input.RequestId));
        var preview = await PreviewAsync(input, cancellationToken);
        if (!string.Equals(preview.PreviewSha256, input.PreviewSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("配置或目标Agent已在预览后发生变化，请重新预览。");
        if (preview.Targets.Any(x => !x.Exists)) throw new ArgumentException("目标Agent不存在。", nameof(input.AgentIds));

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await db.ConfigVersions.SingleOrDefaultAsync(x => x.RequestId == input.RequestId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PreviewSha256, input.PreviewSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("相同RequestId已用于其他配置发布。");
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        var entity = await CreateVersionAsync(
            input.Name, preview.PayloadJson, input.MinimumAgentVersion, NormalizeTargets(input.AgentIds),
            actor, input.RequestId, input.PreviewSha256, input.Reason, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity;
    }

    public async Task<ConfigVersionEntity> RollbackAsync(
        Guid sourceVersionId, RollbackConfigVersionRequest input, string actor, CancellationToken cancellationToken)
    {
        if (input.RequestId == Guid.Empty) throw new ArgumentException("RequestId不能为空。", nameof(input.RequestId));
        var targets = NormalizeTargets(input.AgentIds);
        if (targets.Count == 0) throw new ArgumentException("至少选择一个Agent。", nameof(input.AgentIds));
        var knownTargets = await db.Agents.AsNoTracking().Where(x => targets.Contains(x.Id)).Select(x => x.Id)
            .Concat(db.AgentDeployments.AsNoTracking().Where(x => targets.Contains(x.AgentId)).Select(x => x.AgentId))
            .Distinct().ToListAsync(cancellationToken);
        if (targets.Any(target => !knownTargets.Contains(target, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("目标Agent不存在。", nameof(input.AgentIds));
        var source = await db.ConfigVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceVersionId, cancellationToken)
            ?? throw new ArgumentException("源配置版本不存在。", nameof(sourceVersionId));
        var previewSha = ComputePreviewSha256($"回滚至 v{source.Version}", source.MinimumAgentVersion, targets, source.Sha256);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await db.ConfigVersions.SingleOrDefaultAsync(x => x.RequestId == input.RequestId, cancellationToken);
        if (existing is not null)
        {
            if (existing.RollbackSourceVersionId != source.Id
                || !string.Equals(existing.PreviewSha256, previewSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("相同RequestId已用于其他配置发布或回滚。");
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }
        var entity = await CreateVersionAsync(
            $"回滚至 v{source.Version}", source.PayloadJson, source.MinimumAgentVersion, targets, actor,
            input.RequestId, previewSha, input.Reason, source.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity;
    }

    public async Task<ConfigVersionEntity> SyncInternalAsync(string name, string payloadJson, string minimumAgentVersion,
        IReadOnlyCollection<string> agentIds, string actor, CancellationToken cancellationToken)
    {
        var targets = NormalizeTargets(agentIds);
        if (targets.Count == 0) throw new ArgumentException("至少选择一个Agent。", nameof(agentIds));
        ValidateNameAndVersion(name, minimumAgentVersion);
        var hash = ConfigPackage.ComputeSha256(payloadJson);
        var validation = new ConfigPackage
        {
            AssignmentId = Guid.NewGuid(), AgentId = "internal-sync", Version = 1,
            MinimumAgentVersion = minimumAgentVersion, PayloadJson = payloadJson, Sha256 = hash
        }.Validate();
        if (!validation.IsValid) throw new ArgumentException(string.Join("; ", validation.Errors), nameof(payloadJson));

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var entity = await CreateVersionAsync(
            name, payloadJson, minimumAgentVersion, targets, actor, null, hash, null, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entity;
    }

    private async Task<ConfigVersionEntity> CreateVersionAsync(
        string name, string payloadJson, string minimumAgentVersion, IReadOnlyCollection<string> targets,
        string actor, Guid? requestId, string previewSha256, string? reason, Guid? rollbackSourceVersionId,
        CancellationToken cancellationToken)
    {
        var version = (await db.ConfigVersions.MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var hash = ConfigPackage.ComputeSha256(payloadJson);
        var entity = new ConfigVersionEntity
        {
            Id = Guid.NewGuid(),
            Version = version,
            Name = name.Trim(),
            PayloadJson = payloadJson,
            MinimumAgentVersion = minimumAgentVersion.Trim(),
            Sha256 = hash,
            RequestId = requestId,
            PreviewSha256 = previewSha256,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            RollbackSourceVersionId = rollbackSourceVersionId,
            State = ConfigLifecycleState.Published,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = actor
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
            Actor = actor,
            Action = rollbackSourceVersionId.HasValue ? "config.rollback" : "config.sync",
            Target = entity.Id.ToString(),
            Detail = $"version={version};targets={targets.Count};source={rollbackSourceVersionId}",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private static List<string> NormalizeTargets(IEnumerable<string>? agentIds) =>
        (agentIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    private static void ValidateNameAndVersion(string? name, string? minimumAgentVersion)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("配置名称不能为空且不能超过200个字符。", nameof(name));
        if (!Version.TryParse(minimumAgentVersion, out _))
            throw new ArgumentException("最低Agent版本格式无效。", nameof(minimumAgentVersion));
    }

    private static string ComputePreviewSha256(
        string name, string minimumAgentVersion, IReadOnlyCollection<string> targets, string payloadSha256)
    {
        var value = string.Join('\n', new[]
        {
            name.Trim(), minimumAgentVersion.Trim(), string.Join(',', targets.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)), payloadSha256
        });
        return ConfigPackage.ComputeSha256(value);
    }
}
