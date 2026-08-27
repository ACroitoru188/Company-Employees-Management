using CompanyEmployees.Persistence;

namespace CompanyEmployees.Web.Services;

internal sealed class StandbySynchronizationService(
    IConfiguration configuration,
    DatabaseRuntimeState state,
    IStandbyReplicationService? synchronizer,
    DatabaseReplicationCoordinator replication,
    DatabaseWriteGate writeGate,
    ILogger<StandbySynchronizationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Clamp(
            int.TryParse(configuration["DatabaseFailover:ReplicationIntervalSeconds"], out var seconds)
                ? seconds
                : 2,
            1,
            60);

        // Give migrations and the web host time to finish starting before the first copy.
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        var baselineCompleted = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        do
        {
            var targetAvailable = state.ActiveProviderId == state.PrimaryProviderId
                ? state.SecondaryAvailable
                : state.PrimaryAvailable;
            if (targetAvailable)
            {
                try
                {
                    if (!baselineCompleted
                        && synchronizer != null
                        && state.ActiveProviderId == state.PrimaryProviderId
                        && state.PrimaryAvailable
                        && state.SecondaryAvailable)
                    {
                        using var writeLease = await writeGate.EnterAsync(stoppingToken);
                        await synchronizer.SynchronizeAsync(stoppingToken);
                        baselineCompleted = true;
                    }
                    else
                    {
                        await replication.ReplicateNextBatchAsync(stoppingToken);
                        await replication.RefreshStatusAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not replicate the active database to its standby.");
                }
            }
            else
            {
                try
                {
                    // Continue reporting queued writes even while the standby is offline.
                    await replication.RefreshStatusAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not refresh database replication status.");
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
