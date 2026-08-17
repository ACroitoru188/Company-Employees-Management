using CompanyEmployees.Persistence;

namespace CompanyEmployees.Web.Services;

internal sealed class PostgreSqlStandbySynchronizationService(
    IConfiguration configuration,
    DatabaseRuntimeState state,
    PostgreSqlStandbySynchronizer synchronizer,
    ILogger<PostgreSqlStandbySynchronizationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Clamp(
            int.TryParse(configuration["DatabaseFailover:SynchronizationIntervalSeconds"], out var seconds)
                ? seconds
                : 60,
            15,
            3600);

        // Give migrations and the web host time to finish starting before the first copy.
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        do
        {
            if (state.ActiveProvider == DatabaseProvider.SqlServer
                && state.PrimaryAvailable
                && state.PostgreSqlAvailable)
            {
                try
                {
                    await synchronizer.SynchronizeAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not synchronize the PostgreSQL standby.");
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
