using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Persistence;

public sealed class DatabaseProviderSwitcher(
    IConfiguration configuration,
    DatabaseRuntimeState state,
    PostgreSqlStandbySynchronizer synchronizer,
    ILogger<DatabaseProviderSwitcher> logger)
{
    public async Task SwitchAsync(
        DatabaseProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (provider == DatabaseProvider.SqlServer)
            await DatabaseFailoverSelector.ProbeSqlServerAsync(configuration, cancellationToken);
        else
        {
            await DatabaseFailoverSelector.ProbePostgreSqlAsync(configuration, cancellationToken);
            if (state.PrimaryAvailable)
                await synchronizer.SynchronizeAsync(cancellationToken);
            else
                await PostgreSqlStandbyBootstrapper.EnsureReadyAsync(configuration, cancellationToken);
        }

        state.SelectProvider(provider);
        logger.LogWarning("An administrator selected {DatabaseProvider} as the active database.", provider);
    }
}
