using CompanyEmployees.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Persistence;

public static class DatabaseOutboxSchemaInitializer
{
    /// <summary>
    /// Ensures the <c>DatabaseOutbox</c> table exists by delegating to the provider plugin,
    /// which owns the engine-specific idempotent DDL.
    /// </summary>
    public static Task EnsureCreatedAsync(
        CompanyEmployeesDbContext db,
        IDbProviderPlugin plugin,
        CancellationToken cancellationToken = default) =>
        plugin.CreateOutboxSchemaAsync(db, cancellationToken);
}
