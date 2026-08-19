using CompanyEmployees.Persistence;

namespace CompanyEmployees.Web.Services;

internal sealed class DatabaseAvailabilityMonitor(
    IConfiguration configuration,
    IHostEnvironment environment,
    DatabaseRuntimeState state,
    ILogger<DatabaseAvailabilityMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Clamp(
            int.TryParse(configuration["DatabaseFailover:HealthCheckIntervalSeconds"], out var seconds)
                ? seconds
                : 5,
            2,
            60);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        do
        {
            var simulatedOutage = IsSqlServerOutageSimulated();
            var sqlProbe = simulatedOutage
                ? Task.FromResult<(bool Available, string? Failure)>(
                    (false, "Development outage simulation is active."))
                : ProbeAsync(DatabaseFailoverSelector.ProbeSqlServerAsync, stoppingToken);
            var postgreSqlProbe = ProbeAsync(
                DatabaseFailoverSelector.ProbePostgreSqlAsync,
                stoppingToken);

            // Neither availability check depends on the other. Running them together keeps a
            // slow standby probe from delaying the SQL Server recovery banner.
            await Task.WhenAll(sqlProbe, postgreSqlProbe);
            var (sqlAvailable, sqlFailure) = await sqlProbe;
            var (postgreSqlAvailable, _) = await postgreSqlProbe;

            var wasSqlAvailable = state.PrimaryAvailable;
            state.UpdateAvailability(sqlAvailable, postgreSqlAvailable, sqlFailure);
            if (wasSqlAvailable != sqlAvailable)
            {
                logger.LogWarning(
                    "SQL Server availability changed to {SqlServerAvailable}. Active provider remains {Provider} until an admin changes it.",
                    sqlAvailable,
                    state.ActiveProvider);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private bool IsSqlServerOutageSimulated()
    {
        if (!environment.IsDevelopment())
            return false;

        var configuredPath = configuration["DatabaseFailover:OutageMarkerPath"]
            ?? "../../../.tmp/simulate-sqlserver-down";
        var markerPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
        return File.Exists(markerPath);
    }

    private async Task<(bool Available, string? Failure)> ProbeAsync(
        Func<IConfiguration, CancellationToken, Task> probe,
        CancellationToken cancellationToken)
    {
        try
        {
            await probe(configuration, cancellationToken);
            return (true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
