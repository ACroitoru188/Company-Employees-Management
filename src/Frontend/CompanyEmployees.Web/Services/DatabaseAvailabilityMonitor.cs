using CompanyEmployees.Persistence;
using CompanyEmployees.Persistence.Contracts;

namespace CompanyEmployees.Web.Services;

internal sealed class DatabaseAvailabilityMonitor(
    IConfiguration configuration,
    IHostEnvironment environment,
    DatabaseRuntimeState state,
    IDbProviderPlugin primaryPlugin,
    string primaryConnectionString,
    IDbProviderPlugin? secondaryPlugin,
    string? secondaryConnectionString,
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
            var simulatedOutage = IsPrimaryOutageSimulated();
            var primaryProbe = simulatedOutage
                ? Task.FromResult<(bool Available, string? Failure)>(
                    (false, "Development outage simulation is active."))
                : ProbePluginAsync(primaryPlugin, primaryConnectionString, stoppingToken);
            var secondaryProbe = secondaryPlugin != null && !string.IsNullOrEmpty(secondaryConnectionString)
                ? ProbePluginAsync(secondaryPlugin, secondaryConnectionString, stoppingToken)
                : Task.FromResult<(bool, string?)>((false, "No secondary configured."));

            await Task.WhenAll(primaryProbe, secondaryProbe);
            var (primaryAvailable, primaryFailure) = await primaryProbe;
            var (secondaryAvailable, _) = await secondaryProbe;

            var wasPrimaryAvailable = state.PrimaryAvailable;
            state.UpdateAvailability(primaryAvailable, secondaryAvailable, primaryFailure);
            if (wasPrimaryAvailable != primaryAvailable)
            {
                logger.LogWarning(
                    "Primary database ({PrimaryProvider}) availability changed to {Available}. Active provider remains {ActiveProvider} until an admin changes it.",
                    primaryPlugin.DisplayName,
                    primaryAvailable,
                    state.ActiveProviderId);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private bool IsPrimaryOutageSimulated()
    {
        if (!environment.IsDevelopment())
            return false;

        // Supports both the legacy filename and the provider-agnostic one so existing dev
        // environments do not need to rename the file they already have.
        var configuredPath = configuration["DatabaseFailover:OutageMarkerPath"];
        var candidates = configuredPath != null
            ? [configuredPath]
            : new[] { "../../../.tmp/simulate-primary-down", "../../../.tmp/simulate-sqlserver-down" };
        return candidates.Any(relative =>
            File.Exists(Path.GetFullPath(Path.Combine(environment.ContentRootPath, relative))));
    }

    private async Task<(bool Available, string? Failure)> ProbePluginAsync(
        IDbProviderPlugin plugin,
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            await plugin.TestConnectionAsync(connectionString, cancellationToken);
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
