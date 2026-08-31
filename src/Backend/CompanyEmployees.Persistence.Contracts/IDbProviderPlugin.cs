using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Persistence.Contracts;

/// <summary>
/// Implemented by each database provider plugin DLL loaded at runtime from the Providers/ folder.
/// The host application has zero compile-time dependency on any driver package — all provider-specific
/// code (UseSqlServer, UseNpgsql, etc.) lives exclusively inside the implementing assembly.
/// </summary>
public interface IDbProviderPlugin
{
    /// <summary>Stable machine identifier used in config and state. Examples: "sqlserver", "postgresql".</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the setup wizard. Example: "Microsoft SQL Server".</summary>
    string DisplayName { get; }

    /// <summary>
    /// The EF Core invariant provider name returned by <c>Database.ProviderName</c> for a context
    /// configured by this plugin. Example: "Microsoft.EntityFrameworkCore.SqlServer".
    /// Used by the host to match the active plugin against the DbContext's configured provider
    /// without hardcoding plugin IDs.
    /// </summary>
    string EfProviderName { get; }

    /// <summary>
    /// Fields the setup wizard must collect from the admin to build a valid connection string.
    /// </summary>
    IReadOnlyList<ConnectionField> RequiredFields { get; }

    /// <summary>
    /// Configures <paramref name="options"/> with the provider-specific extension method
    /// (e.g. UseSqlServer / UseNpgsql) and the supplied connection string.
    /// Called both at app startup (to wire up the DI DbContext) and by the replication
    /// subsystem when it needs to build a second DbContext for the standby.
    /// </summary>
    void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString);

    /// <summary>
    /// Opens and immediately closes a connection to verify reachability.
    /// Throws on failure; the caller decides how to handle the exception.
    /// </summary>
    Task TestConnectionAsync(string connectionString, CancellationToken ct = default);

    /// <summary>
    /// Applies schema / seeds to the target database.
    /// SQL Server plugin calls MigrateAsync; PostgreSQL plugin calls EnsureCreatedAsync.
    /// </summary>
    Task ApplyMigrationsAsync(DbContext context, CancellationToken ct = default);

    /// <summary>
    /// Called after a full bulk-insert of data into a target database (e.g. during standby sync).
    /// Allows each provider to perform any post-insert housekeeping that is specific to its engine.
    /// The default no-op is correct for providers that do not need housekeeping.
    /// </summary>
    Task AfterBulkInsertAsync(DbContext context, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Creates (or ensures the existence of) the <c>DatabaseOutbox</c> table and its indexes
    /// using provider-specific DDL. Each engine has its own syntax for idempotent DDL
    /// so the provider is the correct owner of this knowledge.
    /// </summary>
    Task CreateOutboxSchemaAsync(DbContext context, CancellationToken ct = default);
}
