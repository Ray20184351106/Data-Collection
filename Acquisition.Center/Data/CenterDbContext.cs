using Microsoft.EntityFrameworkCore;

namespace Acquisition.Center.Data;

public sealed class CenterDbContext(DbContextOptions<CenterDbContext> options)
    : DbContext(options)
{
    public DbSet<AgentEntity> Agents => Set<AgentEntity>();
    public DbSet<DeviceStatusEntity> DeviceStatuses => Set<DeviceStatusEntity>();
    public DbSet<ProcessedRequestEntity> ProcessedRequests => Set<ProcessedRequestEntity>();
    public DbSet<CollectionRecordEntity> CollectionRecords => Set<CollectionRecordEntity>();
    public DbSet<CommandEntity> Commands => Set<CommandEntity>();
    public DbSet<ConfigVersionEntity> ConfigVersions => Set<ConfigVersionEntity>();
    public DbSet<ConfigAssignmentEntity> ConfigAssignments => Set<ConfigAssignmentEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<AlertEntity> Alerts => Set<AlertEntity>();
    public DbSet<AgentDeploymentEntity> AgentDeployments => Set<AgentDeploymentEntity>();
    public DbSet<AgentDiagnosticSnapshotEntity> AgentDiagnosticSnapshots => Set<AgentDiagnosticSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AgentEntity>().HasKey(x => x.Id);
        builder.Entity<DeviceStatusEntity>().HasIndex(x => new { x.AgentId, x.DeviceId }).IsUnique();
        builder.Entity<ProcessedRequestEntity>().HasKey(x => x.Id);
        builder.Entity<CollectionRecordEntity>().HasIndex(x => new { x.AgentId, x.ProcessedAtUtc });
        builder.Entity<CommandEntity>().HasIndex(x => new { x.AgentId, x.CreatedAtUtc });
        builder.Entity<ConfigVersionEntity>().HasIndex(x => x.Version).IsUnique();
        builder.Entity<ConfigVersionEntity>().HasIndex(x => x.RequestId).IsUnique();
        builder.Entity<ConfigAssignmentEntity>().HasIndex(x => new { x.ConfigVersionId, x.AgentId }).IsUnique();
        builder.Entity<ConfigAssignmentEntity>().HasOne(x => x.ConfigVersion).WithMany().HasForeignKey(x => x.ConfigVersionId);
        builder.Entity<AuditLogEntity>().HasIndex(x => x.CreatedAtUtc);
        builder.Entity<AlertEntity>().HasIndex(x => new { x.AgentId, x.Code, x.AcknowledgedAtUtc });
        builder.Entity<AgentDeploymentEntity>().Property(x => x.AgentId).HasMaxLength(128);
        builder.Entity<AgentDeploymentEntity>().Property(x => x.EnrollmentTokenHash).HasMaxLength(64);
        builder.Entity<AgentDeploymentEntity>().Property(x => x.DeviceCredentialHash).HasMaxLength(64);
        builder.Entity<AgentDeploymentEntity>().HasIndex(x => x.AgentId).IsUnique();
        builder.Entity<AgentDiagnosticSnapshotEntity>().Property(x => x.AgentId).HasMaxLength(128);
        builder.Entity<AgentDiagnosticSnapshotEntity>().Property(x => x.EffectiveConfigSha256).HasMaxLength(64);
        builder.Entity<AgentDiagnosticSnapshotEntity>().HasKey(x => x.AgentId);
    }
}
