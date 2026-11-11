using Fyp_Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Services;

/// <summary>
/// Ensures that shared FypWeb + Fyp_Backend columns and tables exist on startup
/// (e.g. Hiring.Hiring_Date, Interview_Skill, Review_Skill_Rating, Worker_Category.IsPrimary,
/// WorkerNotification Type width) so running FypWeb against an older database works out of the box.
/// </summary>
public static class DatabaseSchemaInitializer
{
    private const string AlignmentSql = @"
IF OBJECT_ID('dbo.Hiring', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Hiring', 'Hiring_Date') IS NULL
       AND COL_LENGTH('dbo.Hiring', 'HiringDate') IS NOT NULL
    BEGIN
        EXEC sp_rename 'dbo.Hiring.HiringDate', 'Hiring_Date', 'COLUMN';
    END;

    IF COL_LENGTH('dbo.Hiring', 'Hiring_Date') IS NULL
    BEGIN
        ALTER TABLE dbo.Hiring ADD Hiring_Date DATETIME NULL;
    END;
END;

IF OBJECT_ID('dbo.Interview_Skill', 'U') IS NULL
   AND OBJECT_ID('dbo.Interview', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.Skills', 'U') IS NOT NULL
BEGIN
    CREATE TABLE dbo.Interview_Skill
    (
        Interview_ID INT NOT NULL,
        Skills_ID    INT NOT NULL,
        CONSTRAINT PK_Interview_Skill PRIMARY KEY (Interview_ID, Skills_ID),
        CONSTRAINT FK_Interview_Skill_Interview FOREIGN KEY (Interview_ID)
            REFERENCES dbo.Interview (Interview_ID) ON DELETE CASCADE,
        CONSTRAINT FK_Interview_Skill_Skills FOREIGN KEY (Skills_ID)
            REFERENCES dbo.Skills (Skills_ID) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID('dbo.Review_Skill_Rating', 'U') IS NULL
   AND OBJECT_ID('dbo.Reviews', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.Skills', 'U') IS NOT NULL
BEGIN
    CREATE TABLE dbo.Review_Skill_Rating
    (
        Review_ID INT NOT NULL,
        Skills_ID INT NOT NULL,
        Score     INT NOT NULL,
        CONSTRAINT PK_Review_Skill_Rating PRIMARY KEY (Review_ID, Skills_ID),
        CONSTRAINT CK_Review_Skill_Rating_Score CHECK (Score BETWEEN 1 AND 5),
        CONSTRAINT FK_Review_Skill_Rating_Review FOREIGN KEY (Review_ID)
            REFERENCES dbo.Reviews (Review_ID) ON DELETE CASCADE,
        CONSTRAINT FK_Review_Skill_Rating_Skills FOREIGN KEY (Skills_ID)
            REFERENCES dbo.Skills (Skills_ID) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID('dbo.Worker_Category', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Worker_Category', 'IsPrimary') IS NULL
    BEGIN
        ALTER TABLE dbo.Worker_Category
            ADD IsPrimary BIT NOT NULL CONSTRAINT DF_Worker_Category_IsPrimary DEFAULT (0);
    END;

    EXEC sp_executesql N'
        ;WITH FirstCategoryPerWorker AS (
            SELECT Worker_ID, MIN(Category_ID) AS PrimaryCategoryId
            FROM dbo.Worker_Category
            GROUP BY Worker_ID
            HAVING MAX(CAST(IsPrimary AS INT)) = 0
        )
        UPDATE wc
        SET wc.IsPrimary = 1
        FROM dbo.Worker_Category wc
        INNER JOIN FirstCategoryPerWorker fc
            ON wc.Worker_ID = fc.Worker_ID
           AND wc.Category_ID = fc.PrimaryCategoryId;
    ';
END;

IF OBJECT_ID('dbo.CourseCertificates', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.CourseCertificates', 'CertificateUrl') IS NULL
        ALTER TABLE dbo.CourseCertificates ADD CertificateUrl NVARCHAR(500) NULL;

    IF COL_LENGTH('dbo.CourseCertificates', 'IsRevoked') IS NULL
        ALTER TABLE dbo.CourseCertificates
            ADD IsRevoked BIT NOT NULL CONSTRAINT DF_CourseCertificates_IsRevoked DEFAULT (0);

    IF COL_LENGTH('dbo.CourseCertificates', 'RevokedAtUtc') IS NULL
        ALTER TABLE dbo.CourseCertificates ADD RevokedAtUtc DATETIME2 NULL;

    IF COL_LENGTH('dbo.CourseCertificates', 'RevocationReason') IS NULL
        ALTER TABLE dbo.CourseCertificates ADD RevocationReason NVARCHAR(500) NULL;
END;

IF OBJECT_ID('dbo.Interview', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.Interview', 'IsExclusive') IS NULL
BEGIN
    ALTER TABLE dbo.Interview
        ADD IsExclusive BIT NOT NULL CONSTRAINT DF_Interview_IsExclusive DEFAULT (0);
END;

IF OBJECT_ID('dbo.Resignation', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Resignation', 'NoticeEndsAtUtc') IS NULL
        ALTER TABLE dbo.Resignation ADD NoticeEndsAtUtc DATETIME2(7) NULL;

    IF COL_LENGTH('dbo.Resignation', 'AcceptedAtUtc') IS NULL
        ALTER TABLE dbo.Resignation ADD AcceptedAtUtc DATETIME2(7) NULL;

    IF COL_LENGTH('dbo.Resignation', 'AcceptanceMode') IS NULL
        ALTER TABLE dbo.Resignation ADD AcceptanceMode VARCHAR(16) NULL;
END;

IF OBJECT_ID('dbo.WorkerNotification', 'U') IS NULL
   AND OBJECT_ID('dbo.Worker', 'U') IS NOT NULL
BEGIN
    CREATE TABLE dbo.WorkerNotification
    (
        WorkerId        INT           NOT NULL,
        NotificationKey VARCHAR(96)   NOT NULL,
        [Type]          VARCHAR(32)   NOT NULL,
        SourceId        INT           NOT NULL,
        InterviewId     INT           NULL,
        Title           NVARCHAR(160) NOT NULL,
        [Message]       NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc    DATETIME2(7)  NOT NULL,
        ReadAtUtc       DATETIME2(7)  NULL,
        CONSTRAINT PK_WorkerNotification PRIMARY KEY (WorkerId, NotificationKey),
        CONSTRAINT FK_WorkerNotification_Worker FOREIGN KEY (WorkerId)
            REFERENCES dbo.Worker (Worker_ID) ON DELETE CASCADE
    );

    CREATE INDEX IX_WorkerNotification_Feed
        ON dbo.WorkerNotification (WorkerId ASC, CreatedAtUtc DESC, NotificationKey DESC);

    CREATE INDEX IX_WorkerNotification_Unread
        ON dbo.WorkerNotification (WorkerId ASC, ReadAtUtc ASC);
END
ELSE IF OBJECT_ID('dbo.WorkerNotification', 'U') IS NOT NULL
    AND COL_LENGTH('dbo.WorkerNotification', 'Type') < 32
BEGIN
    ALTER TABLE dbo.WorkerNotification ALTER COLUMN [Type] VARCHAR(32) NOT NULL;
END;
";

    public static async Task EnsureSharedSchemaAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseSchemaInitializer");
        var db = scope.ServiceProvider.GetRequiredService<Fyp1Context>();

        try
        {
            await db.Database.ExecuteSqlRawAsync(AlignmentSql);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Automatic shared-DB schema check could not apply DDL changes. Run Database/FypWeb_SharedDb_Migration.sql in SSMS.");
        }
    }
}
