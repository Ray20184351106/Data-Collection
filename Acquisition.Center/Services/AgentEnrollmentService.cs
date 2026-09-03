using System.Data;
using Acquisition.Center.Data;
using Acquisition.Center.Security;
using Acquisition.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Acquisition.Center.Services;

public sealed class AgentEnrollmentService
{
    private readonly CenterDbContext _db;
    private readonly TimeProvider _timeProvider;

    public AgentEnrollmentService(CenterDbContext db) : this(db, TimeProvider.System) { }
    public AgentEnrollmentService(CenterDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<AgentEnrollmentResponse> EnrollAsync(AgentEnrollmentRequest input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.AgentId) || string.IsNullOrWhiteSpace(input.EnrollmentToken))
            throw new ArgumentException("AgentId和一次性注册令牌不能为空。", nameof(input));
        var now = _timeProvider.GetUtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var deployment = await _db.AgentDeployments.SingleOrDefaultAsync(
            x => x.AgentId == input.AgentId.Trim().ToUpper(), cancellationToken)
            ?? throw new InvalidOperationException("未找到对应的设备部署记录。");
        if (deployment.EnrollmentConsumedAtUtc.HasValue || deployment.EnrolledAtUtc.HasValue)
            throw new InvalidOperationException("一次性注册令牌已使用。");
        if (deployment.EnrollmentExpiresAtUtc < now)
            throw new InvalidOperationException("一次性注册令牌已过期，请重新生成设备包。");
        if (!DeviceCredentialHasher.IsValid(deployment.EnrollmentTokenHash, input.EnrollmentToken))
            throw new InvalidOperationException("一次性注册令牌无效。");

        var credential = DeviceCredentialHasher.CreateSecret();
        deployment.EnrollmentConsumedAtUtc = now;
        deployment.EnrolledAtUtc = now;
        deployment.DeviceCredentialHash = DeviceCredentialHasher.Hash(credential);
        deployment.State = AgentDeploymentLifecycleState.Enrolled;
        _db.AuditLogs.Add(new AuditLogEntity
        {
            Actor = deployment.AgentId,
            Action = "agent.enroll",
            Target = deployment.Id.ToString(),
            CreatedAtUtc = now
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AgentEnrollmentResponse
        {
            AgentId = deployment.AgentId,
            DeviceCredential = credential,
            EnrolledAtUtc = now
        };
    }
}

public sealed class AgentCredentialService(CenterDbContext db)
{
    public Task<bool> HasDeploymentAsync(string? agentId, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(agentId)
            ? Task.FromResult(false)
            : db.AgentDeployments.AsNoTracking().AnyAsync(x => x.AgentId == agentId.Trim().ToUpper(), cancellationToken);

    public async Task<bool> IsValidAsync(string? agentId, string? suppliedCredential, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(suppliedCredential)) return false;
        var hash = await db.AgentDeployments.AsNoTracking()
            .Where(x => x.AgentId == agentId.Trim().ToUpper() && x.DeviceCredentialHash != null)
            .Select(x => x.DeviceCredentialHash)
            .SingleOrDefaultAsync(cancellationToken);
        return DeviceCredentialHasher.IsValid(hash, suppliedCredential);
    }
}
