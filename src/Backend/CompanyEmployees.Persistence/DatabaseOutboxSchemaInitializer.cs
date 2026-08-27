using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Persistence;

public static class DatabaseOutboxSchemaInitializer
{
    public static async Task EnsureCreatedAsync(
        CompanyEmployeesDbContext db,
        CancellationToken cancellationToken = default)
    {
        var sql = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? PostgreSql
            : SqlServer;
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private const string PostgreSql = """
        CREATE TABLE IF NOT EXISTS "DatabaseOutbox" (
            "Id" uuid NOT NULL,
            "BatchId" uuid NOT NULL,
            "BatchOrder" integer NOT NULL,
            "SourceProvider" character varying(32) NOT NULL,
            "EntityType" character varying(512) NOT NULL,
            "Operation" character varying(16) NOT NULL,
            "KeyJson" text NOT NULL,
            "PayloadJson" text NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "ProcessedAtUtc" timestamp with time zone NULL,
            "AttemptCount" integer NOT NULL,
            "LastError" character varying(2000) NULL,
            CONSTRAINT "PK_DatabaseOutbox" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_DatabaseOutbox_ProcessedAtUtc_CreatedAtUtc"
            ON "DatabaseOutbox" ("ProcessedAtUtc", "CreatedAtUtc");
        CREATE INDEX IF NOT EXISTS "IX_DatabaseOutbox_BatchId_BatchOrder"
            ON "DatabaseOutbox" ("BatchId", "BatchOrder");
        """;

    private const string SqlServer = """
        IF OBJECT_ID(N'[DatabaseOutbox]', N'U') IS NULL
        BEGIN
            CREATE TABLE [DatabaseOutbox] (
                [Id] uniqueidentifier NOT NULL,
                [BatchId] uniqueidentifier NOT NULL,
                [BatchOrder] int NOT NULL,
                [SourceProvider] nvarchar(32) NOT NULL,
                [EntityType] nvarchar(512) NOT NULL,
                [Operation] nvarchar(16) NOT NULL,
                [KeyJson] nvarchar(max) NOT NULL,
                [PayloadJson] nvarchar(max) NULL,
                [CreatedAtUtc] datetime2 NOT NULL,
                [ProcessedAtUtc] datetime2 NULL,
                [AttemptCount] int NOT NULL,
                [LastError] nvarchar(2000) NULL,
                CONSTRAINT [PK_DatabaseOutbox] PRIMARY KEY ([Id])
            );
            CREATE INDEX [IX_DatabaseOutbox_ProcessedAtUtc_CreatedAtUtc]
                ON [DatabaseOutbox] ([ProcessedAtUtc], [CreatedAtUtc]);
            CREATE INDEX [IX_DatabaseOutbox_BatchId_BatchOrder]
                ON [DatabaseOutbox] ([BatchId], [BatchOrder]);
        END
        """;
}
