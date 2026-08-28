using CompanyEmployees.Persistence.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CompanyEmployees.Persistence.Providers.SqlServer;

/// <summary>
/// Runtime plugin that configures CompanyEmployees to use Microsoft SQL Server.
/// </summary>
public sealed class SqlServerProviderPlugin : IDbProviderPlugin
{
    public string Id => "sqlserver";
    public string DisplayName => "Microsoft SQL Server";
    public string EfProviderName => "Microsoft.EntityFrameworkCore.SqlServer";

    public IReadOnlyList<ConnectionField> RequiredFields =>
    [
        new("Server",                "Server / host",              IsSecret: false, DefaultValue: "localhost,1433"),
        new("Database",              "Database name",              IsSecret: false, DefaultValue: "CompanyEmployees"),
        new("User Id",               "Username",                   IsSecret: false, DefaultValue: "sa"),
        new("Password",              "Password",                   IsSecret: true,  DefaultValue: "CompanyEmployees_dev_2026!"),
        new("TrustServerCertificate","Trust server certificate",   IsSecret: false, DefaultValue: "True"),
        new("MultipleActiveResultSets", "Multiple Active Result Sets", IsSecret: false, DefaultValue: "true"),
    ];

    public void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(
            connectionString,
            sql => sql.MigrationsAssembly(typeof(SqlServerProviderPlugin).Assembly.GetName().Name));

    /// <inheritdoc />
    public async Task TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5,
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
    }

    /// <inheritdoc />
    public async Task ApplyMigrationsAsync(DbContext context, CancellationToken ct = default)
    {
        var creator = context.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(ct))
            await creator.CreateAsync(ct);

        if (!await creator.HasTablesAsync(ct))
        {
            try
            {
                await context.Database.MigrateAsync(ct);
            }
            catch
            {
                try
                {
                    await creator.CreateTablesAsync(ct);
                }
                catch (SqlException ex) when (ex.Number == 2714) // object already exists
                {
                    // Tables exist, safe to proceed
                }
            }
        }
        else
        {
            try
            {
                await context.Database.MigrateAsync(ct);
            }
            catch (SqlException ex) when (ex.Number == 2714) // object already exists
            {
                // Tables already created via standby replication / EnsureCreated
            }
        }
    }

    /// <inheritdoc />
    public Task CreateOutboxSchemaAsync(DbContext context, CancellationToken ct = default) =>
        context.Database.ExecuteSqlRawAsync("""
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
            """, ct);
}
