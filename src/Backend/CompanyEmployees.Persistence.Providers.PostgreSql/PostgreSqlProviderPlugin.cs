using CompanyEmployees.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CompanyEmployees.Persistence.Providers.PostgreSql;

/// <summary>
/// Runtime plugin that configures CompanyEmployees to use PostgreSQL.
/// </summary>
public sealed class PostgreSqlProviderPlugin : IDbProviderPlugin
{
    public string Id => "postgresql";
    public string DisplayName => "PostgreSQL";
    public string EfProviderName => "Npgsql.EntityFrameworkCore.PostgreSQL";

    public IReadOnlyList<ConnectionField> RequiredFields =>
    [
        new("Host",     "Host",          IsSecret: false, DefaultValue: "localhost"),
        new("Port",     "Port",          IsSecret: false, DefaultValue: "5432"),
        new("Database", "Database name", IsSecret: false, DefaultValue: "company_employees"),
        new("Username", "Username",      IsSecret: false, DefaultValue: "company_app"),
        new("Password", "Password",      IsSecret: true,  DefaultValue: "company_dev_password"),
    ];

    /// <inheritdoc />
    public void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString);

    /// <inheritdoc />
    public async Task TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
    }

    public async Task ApplyMigrationsAsync(DbContext context, CancellationToken ct = default)
    {
        var creator = context.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(ct))
            await creator.CreateAsync(ct);

        try
        {
            await creator.CreateTablesAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P07")
        {
            // Table already exists, safe to ignore
        }
        catch
        {
            await context.Database.EnsureCreatedAsync(ct);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// PostgreSQL uses sequences for serial/identity columns. When rows are bulk-inserted
    /// with explicit primary key values the sequence is not advanced, so subsequent inserts
    /// would collide. This resets each affected sequence to the current MAX id.
    /// </remarks>
    public Task AfterBulkInsertAsync(DbContext context, CancellationToken ct = default) =>
        context.Database.ExecuteSqlRawAsync("""
            SELECT setval(
                pg_get_serial_sequence('"AspNetUserClaims"', 'Id'),
                COALESCE(MAX("Id"), 1),
                COUNT(*) > 0)
            FROM "AspNetUserClaims";
            SELECT setval(
                pg_get_serial_sequence('"AspNetRoleClaims"', 'Id'),
                COALESCE(MAX("Id"), 1),
                COUNT(*) > 0)
            FROM "AspNetRoleClaims";
            """, ct);

    /// <inheritdoc />
    public Task CreateOutboxSchemaAsync(DbContext context, CancellationToken ct = default) =>
        context.Database.ExecuteSqlRawAsync("""
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
            """, ct);
}
