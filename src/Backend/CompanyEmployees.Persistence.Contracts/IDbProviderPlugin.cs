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
}
