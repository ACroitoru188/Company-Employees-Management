using CompanyEmployees.Persistence;
using CompanyEmployees.Persistence.Contracts;
using CompanyEmployees.Web.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompanyEmployees.Web.Setup;

public static class DatabaseStartupInitializer
{
    public static async Task<(long MigrationsMs, long SeedingMs, long StandbyBootstrapMs)> InitializeAsync(
        WebApplication app,
        DatabaseRuntimeState databaseState,
        DatabaseProviderCatalog catalog,
        IDbProviderPlugin primaryPlugin,
        IDbProviderPlugin? secondaryPlugin,
        string? secondaryConnectionString,
        IConfiguration configuration)
    {
        long migrationsMs = 0;
        long seedingMs = 0;
        long standbyBootstrapMs = 0;

        if (databaseState.IsFailoverActive)
        {
            var swStandby = System.Diagnostics.Stopwatch.StartNew();
            await StandbyBootstrapper.EnsureReadyAsync(
                secondaryPlugin!,
                secondaryConnectionString!,
                configuration);
            standbyBootstrapMs = swStandby.ElapsedMilliseconds;
        }
        else if (app.Environment.IsDevelopment())
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanyEmployeesDbContext>();
            var activePlugin = catalog.FindById(databaseState.ActiveProviderId) ?? primaryPlugin;

            var swMig = System.Diagnostics.Stopwatch.StartNew();
            await activePlugin.ApplyMigrationsAsync(db);
            await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(db, activePlugin);
            migrationsMs = swMig.ElapsedMilliseconds;

            var swSeed = System.Diagnostics.Stopwatch.StartNew();
            await DatabaseSeeder.SeedAsync(db);
            seedingMs = swSeed.ElapsedMilliseconds;
        }

        // Production migrations can be applied out of process, but the cross-provider outbox is
        // deliberately provider-managed and must exist before the first business write.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompanyEmployeesDbContext>();
            var activePlugin = catalog.FindById(databaseState.ActiveProviderId) ?? primaryPlugin;
            await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(db, activePlugin);
        }

        return (migrationsMs, seedingMs, standbyBootstrapMs);
    }
}
