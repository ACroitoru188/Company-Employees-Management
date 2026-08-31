using CompanyEmployees.Persistence.Contracts;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Persistence;

public sealed class DatabaseProviderSwitcher(
    DatabaseRuntimeState state,
    IStandbyReplicationService? replicationService,
    DatabaseReplicationCoordinator replication,
    DatabaseWriteGate writeGate,
    ILogger<DatabaseProviderSwitcher> logger)
{
    public async Task SwitchAsync(
        string targetProviderId,
        IDbProviderPlugin targetPlugin,
        string targetConnectionString,
        CancellationToken cancellationToken = default)
    {
        if (targetProviderId == state.ActiveProviderId)
            return;

        using var writeLease = await writeGate.EnterAsync(cancellationToken);

        await targetPlugin.TestConnectionAsync(targetConnectionString, cancellationToken);

        var switchingToPrimary = targetProviderId == state.PrimaryProviderId;
        if (switchingToPrimary)
        {
            // Failback is stricter than failover: every write made during the outage
            // must reach the primary before it becomes active again.
            await replication.DrainAsync(cancellationToken);
        }
        else
        {
            // Switching to secondary: ensure its schema is ready, then sync if possible.
            if (state.PrimaryAvailable && replicationService != null
                && replicationService.CanReplicate(state.PrimaryProviderId, targetProviderId))
            {
                if (state.LastSynchronizedUtc == null)
                    await replicationService.SynchronizeAsync(cancellationToken);
                else
                    await replication.DrainAsync(cancellationToken);
            }
        }

        state.SelectProvider(targetProviderId, targetPlugin.EfProviderName);
        logger.LogWarning(
            "An administrator selected {ProviderId} ({DisplayName}) as the active database.",
            targetProviderId,
            targetPlugin.DisplayName);
    }
}
