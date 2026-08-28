using CompanyEmployees.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyEmployees.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistenceLayer(
        this IServiceCollection services,
        IDbProviderPlugin activePlugin,
        string primaryConnectionString,
        IDbProviderPlugin? secondaryPlugin,
        string? secondaryConnectionString,
        DatabaseRuntimeState databaseState)
    {
        services.AddSingleton(databaseState);
        services.AddSingleton<DatabaseWriteGate>();
        services.AddSingleton<DatabaseReplicationCoordinator>(sp =>
            new DatabaseReplicationCoordinator(
                databaseState,
                activePlugin,
                primaryConnectionString,
                secondaryPlugin ?? activePlugin,
                secondaryConnectionString ?? primaryConnectionString,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DatabaseReplicationCoordinator>>()));
        services.AddSingleton<DatabaseProviderSwitcher>();

        if (secondaryPlugin != null && !string.IsNullOrEmpty(secondaryConnectionString))
        {
            services.AddSingleton<IStandbyReplicationService>(sp =>
                new StandbySynchronizer(
                    activePlugin,
                    primaryConnectionString,
                    secondaryPlugin,
                    secondaryConnectionString,
                    databaseState,
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StandbySynchronizer>>()));
            services.AddSingleton<StandbySynchronizer>(sp =>
                (StandbySynchronizer)sp.GetRequiredService<IStandbyReplicationService>());
        }
        else
        {
            services.AddSingleton<IStandbyReplicationService, NoOpStandbyReplicationService>();
        }

        services.AddDbContext<CompanyEmployeesDbContext>(options =>
            activePlugin.ConfigureDbContext(options, primaryConnectionString));

        return services;
    }
}

internal sealed class NoOpStandbyReplicationService : IStandbyReplicationService
{
    public bool CanReplicate(string primaryId, string secondaryId) => false;
    public Task SynchronizeAsync(CancellationToken ct = default) => Task.CompletedTask;
}
