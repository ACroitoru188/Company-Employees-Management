using CompanyEmployees.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CompanyEmployees.Persistence;

public static class StandbyBootstrapper
{
    public static async Task EnsureReadyAsync(
        IDbProviderPlugin secondaryPlugin,
        string secondaryConnectionString,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CompanyEmployeesDbContext>();
        secondaryPlugin.ConfigureDbContext(optionsBuilder, secondaryConnectionString);

        await using var db = new CompanyEmployeesDbContext(optionsBuilder.Options);
        db.SuppressOutboxCapture = true;
        await secondaryPlugin.ApplyMigrationsAsync(db, cancellationToken);
        await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(db, secondaryPlugin, cancellationToken);

        // Seed base data and demo accounts on standby
        db.SuppressOutboxCapture = false;
        await DatabaseSeeder.SeedAsync(db, cancellationToken);
    }
}
