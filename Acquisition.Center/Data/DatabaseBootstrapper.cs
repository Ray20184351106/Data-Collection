using Microsoft.EntityFrameworkCore;

namespace Acquisition.Center.Data;

public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(CenterDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('ConfigAssignments', 'IsReleased') IS NOT NULL
                EXEC sp_executesql N'UPDATE ConfigAssignments SET State = 4 WHERE IsReleased = 0 AND State IS NULL';

            IF EXISTS (SELECT 1 FROM ConfigVersions WHERE State = 3)
            BEGIN
                UPDATE ConfigVersions SET State = 99 WHERE State <> 3;
                UPDATE ConfigVersions SET State = 0 WHERE State = 3;
            END

            IF OBJECT_ID(N'Alerts', N'U') IS NULL
            BEGIN
                CREATE TABLE Alerts (
                    Id uniqueidentifier NOT NULL PRIMARY KEY,
                    AgentId nvarchar(450) NOT NULL,
                    DeviceId nvarchar(max) NULL,
                    Code nvarchar(450) NOT NULL,
                    Severity nvarchar(max) NOT NULL,
                    Message nvarchar(max) NOT NULL,
                    CreatedAtUtc datetimeoffset NOT NULL,
                    AcknowledgedAtUtc datetimeoffset NULL,
                    AcknowledgedBy nvarchar(max) NULL
                );
                CREATE INDEX IX_Alerts_AgentId_Code_AcknowledgedAtUtc ON Alerts(AgentId, Code, AcknowledgedAtUtc);
            END

            IF COL_LENGTH('ConfigVersions', 'RequestId') IS NULL
                ALTER TABLE ConfigVersions ADD RequestId uniqueidentifier NULL;
            IF COL_LENGTH('ConfigVersions', 'PreviewSha256') IS NULL
                ALTER TABLE ConfigVersions ADD PreviewSha256 nvarchar(max) NOT NULL CONSTRAINT DF_ConfigVersions_PreviewSha256 DEFAULT N'';
            IF COL_LENGTH('ConfigVersions', 'Reason') IS NULL
                ALTER TABLE ConfigVersions ADD Reason nvarchar(max) NULL;
            IF COL_LENGTH('ConfigVersions', 'RollbackSourceVersionId') IS NULL
                ALTER TABLE ConfigVersions ADD RollbackSourceVersionId uniqueidentifier NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConfigVersions_RequestId' AND object_id = OBJECT_ID(N'ConfigVersions'))
                CREATE UNIQUE INDEX IX_ConfigVersions_RequestId ON ConfigVersions(RequestId) WHERE RequestId IS NOT NULL;

            IF COL_LENGTH('ConfigAssignments', 'EffectiveVersion') IS NULL
                ALTER TABLE ConfigAssignments ADD EffectiveVersion int NULL;
            IF COL_LENGTH('ConfigAssignments', 'EffectiveSha256') IS NULL
                ALTER TABLE ConfigAssignments ADD EffectiveSha256 nvarchar(max) NULL;
            IF COL_LENGTH('ConfigAssignments', 'EffectiveObservedAtUtc') IS NULL
                ALTER TABLE ConfigAssignments ADD EffectiveObservedAtUtc datetimeoffset NULL;
            IF COL_LENGTH('DeviceStatuses', 'ObservedAtUtc') IS NULL
                ALTER TABLE DeviceStatuses ADD ObservedAtUtc datetimeoffset NULL;

            IF OBJECT_ID(N'AgentDeployments', N'U') IS NULL
            BEGIN
                CREATE TABLE AgentDeployments (
                    Id uniqueidentifier NOT NULL PRIMARY KEY,
                    AgentId nvarchar(128) NOT NULL,
                    Site nvarchar(max) NOT NULL,
                    Building nvarchar(max) NOT NULL,
                    Line nvarchar(max) NOT NULL,
                    AcquisitionAppDirectory nvarchar(max) NOT NULL,
                    StartWinFormsOnLogon bit NOT NULL,
                    State int NOT NULL,
                    EnrollmentTokenHash nvarchar(64) NOT NULL,
                    EnrollmentExpiresAtUtc datetimeoffset NOT NULL,
                    EnrollmentConsumedAtUtc datetimeoffset NULL,
                    DeviceCredentialHash nvarchar(64) NULL,
                    PackageIssuedAtUtc datetimeoffset NOT NULL,
                    EnrolledAtUtc datetimeoffset NULL,
                    FirstHeartbeatAtUtc datetimeoffset NULL,
                    LastHeartbeatAtUtc datetimeoffset NULL,
                    CreatedBy nvarchar(max) NOT NULL
                );
                CREATE UNIQUE INDEX IX_AgentDeployments_AgentId ON AgentDeployments(AgentId);
            END

            IF OBJECT_ID(N'AgentDiagnosticSnapshots', N'U') IS NULL
            BEGIN
                CREATE TABLE AgentDiagnosticSnapshots (
                    AgentId nvarchar(128) NOT NULL PRIMARY KEY,
                    ObservedAtUtc datetimeoffset NOT NULL,
                    IsWindowsService bit NULL,
                    ProcessStartedAtUtc datetimeoffset NULL,
                    ProcessPath nvarchar(max) NULL,
                    LocalDatabasePath nvarchar(max) NULL,
                    LocalDatabaseState int NOT NULL,
                    LegacyDatabasePath nvarchar(max) NULL,
                    LegacyDatabaseExists bit NULL,
                    LegacyConfigPath nvarchar(max) NULL,
                    LegacyConfigExists bit NULL,
                    LegacyExecutablePath nvarchar(max) NULL,
                    WinFormsProcessRunning bit NULL,
                    WinFormsLastSeenAtUtc datetimeoffset NULL,
                    EffectiveConfigVersion int NULL,
                    EffectiveConfigSha256 nvarchar(64) NULL,
                    LastErrorSummary nvarchar(max) NULL
                );
            END
            """, cancellationToken);
    }
}
