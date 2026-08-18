using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Persistence;

public sealed class DatabaseProviderSwitcher(
    IConfiguration configuration,
    DatabaseRuntimeState state,
    PostgreSqlStandbySynchronizer synchronizer,
    DatabaseReplicationCoordinator replication,
    DatabaseWriteGate writeGate,
    ILogger<DatabaseProviderSwitcher> logger)
{
    public async Task SwitchAsync(
        DatabaseProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (provider == state.ActiveProvider)
            return;

        using var writeLease = await writeGate.EnterAsync(cancellationToken);

        if (provider == DatabaseProvider.SqlServer)
        {
            await DatabaseFailoverSelector.ProbeSqlServerAsync(configuration, cancellationToken);
            // Failback is deliberately stricter than failover: PostgreSQL is still healthy,
            // so every write made during the outage must reach SQL Server before it is active.
            await replication.DrainAsync(cancellationToken);
        }
        else
        {
            await DatabaseFailoverSelector.ProbePostgreSqlAsync(configuration, cancellationToken);
            if (state.PrimaryAvailable)
            {
                if (state.LastSynchronizedUtc == null)
                    await synchronizer.SynchronizeAsync(cancellationToken);
                else
                    await replication.DrainAsync(cancellationToken);
            }
            else
                await PostgreSqlStandbyBootstrapper.EnsureReadyAsync(configuration, cancellationToken);
        }

        state.SelectProvider(provider);
        logger.LogWarning("An administrator selected {DatabaseProvider} as the active database.", provider);
    }
}
