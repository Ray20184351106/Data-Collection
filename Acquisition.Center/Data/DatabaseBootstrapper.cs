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
            """, cancellationToken);
    }
}
