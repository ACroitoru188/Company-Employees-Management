using CompanyEmployees.Persistence;
using CompanyEmployees.Persistence.Contracts;
using CompanyEmployees.Web.Plugins;
using CompanyEmployees.Web.Setup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CompanyEmployees.Web.Endpoints;

public record StartupTimings(
    long TotalStartupMs,
    long FailoverSelectMs,
    long MigrationsMs,
    long SeedingMs,
    long StandbyBootstrapMs);

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(
        this IEndpointRouteBuilder app,
        DatabaseRuntimeState? databaseState,
        StartupTimings timings,
        IDbProviderPlugin? primaryPlugin,
        string primaryConnectionString,
        IDbProviderPlugin? secondaryPlugin,
        string? secondaryConnectionString,
        IConfiguration configuration)
    {
        app.MapGet("/api/health", (ProviderBenchmarkStore store) =>
        {
            var comparisonReport = store.GetReport();
            return Results.Ok(new
            {
                status = "healthy",
                activeProvider = databaseState?.ActiveProviderId ?? "None",
                secondaryProvider = databaseState?.SecondaryProviderId,
                isFailoverActive = databaseState?.IsFailoverActive ?? false,
                startupMetrics = new
                {
                    totalStartupMs = timings.TotalStartupMs,
                    failoverSelectMs = timings.FailoverSelectMs,
                    migrationsMs = timings.MigrationsMs,
                    seedingMs = timings.SeedingMs,
                    standbyBootstrapMs = timings.StandbyBootstrapMs
                },
                providerComparison = comparisonReport
            });
        }).AllowAnonymous();

        app.MapGet("/api/health/benchmark", async (
            DatabaseProviderCatalog cat,
            CancellationToken ct) =>
        {
            var candidates = new List<(string Id, string DisplayName, IDbProviderPlugin Plugin, string ConnectionString)>();

            if (primaryPlugin != null && !string.IsNullOrEmpty(primaryConnectionString))
                candidates.Add((primaryPlugin.Id, primaryPlugin.DisplayName, primaryPlugin, primaryConnectionString));

            if (secondaryPlugin != null && !string.IsNullOrEmpty(secondaryConnectionString))
                candidates.Add((secondaryPlugin.Id, secondaryPlugin.DisplayName, secondaryPlugin, secondaryConnectionString));

            // Also include any other available plugins with a connection string configured
            foreach (var p in cat.GetAvailable())
            {
                if (candidates.Any(c => c.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var cs = configuration.GetConnectionString(p.Id)
                    ?? (p.Id.Equals("sqlserver", StringComparison.OrdinalIgnoreCase) ? configuration.GetConnectionString("Default") : null);

                if (!string.IsNullOrEmpty(cs))
                    candidates.Add((p.Id, p.DisplayName, p, cs));
            }

            var providerResults = new Dictionary<string, object>();
            var latencies = new Dictionary<string, (long PingMs, long QueryMs)>();

            foreach (var (id, displayName, plugin, cs) in candidates)
            {
                try
                {
                    var swPing = System.Diagnostics.Stopwatch.StartNew();
                    using var pingCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await plugin.TestConnectionAsync(cs, pingCts.Token);
                    var pingMs = swPing.ElapsedMilliseconds;

                    var swQuery = System.Diagnostics.Stopwatch.StartNew();
                    var optionsBuilder = new DbContextOptionsBuilder<CompanyEmployeesDbContext>();
                    plugin.ConfigureDbContext(optionsBuilder, cs);
                    await using var db = new CompanyEmployeesDbContext(optionsBuilder.Options);

                    int? userCount = null;
                    try
                    {
                        using var queryCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        userCount = await db.Users.CountAsync(queryCts.Token);
                    }
                    catch
                    {
                        // In case schema is not yet created on this database
                    }
                    var queryMs = swQuery.ElapsedMilliseconds;

                    latencies[id] = (pingMs, queryMs);
                    providerResults[id] = new
                    {
                        displayName,
                        available = true,
                        connectionPingMs = pingMs,
                        queryLatencyMs = queryMs,
                        totalRoundTripMs = pingMs + queryMs,
                        userCount
                    };
                }
                catch (Exception ex)
                {
                    providerResults[id] = new
                    {
                        displayName,
                        available = false,
                        error = ex.Message
                    };
                }
            }

            var liveAnalysis = new Dictionary<string, object>();
            var successful = latencies.Where(kv => providerResults.TryGetValue(kv.Key, out var r) && (bool)r.GetType().GetProperty("available")!.GetValue(r)!).ToList();
            if (successful.Count >= 2)
            {
                var fastestConn = successful.OrderBy(s => s.Value.PingMs).First();
                var slowestConn = successful.OrderBy(s => s.Value.PingMs).Last();
                var fastestQuery = successful.OrderBy(s => s.Value.QueryMs).First();
                var slowestQuery = successful.OrderBy(s => s.Value.QueryMs).Last();

                liveAnalysis["fastestConnection"] = $"{fastestConn.Key} ({fastestConn.Value.PingMs}ms vs {slowestConn.Key} {slowestConn.Value.PingMs}ms)";
                liveAnalysis["fastestQuery"] = $"{fastestQuery.Key} ({fastestQuery.Value.QueryMs}ms vs {slowestQuery.Key} {slowestQuery.Value.QueryMs}ms)";
            }

            return Results.Ok(new
            {
                status = "success",
                testedUtc = DateTime.UtcNow,
                providers = providerResults,
                liveComparison = liveAnalysis
            });
        }).AllowAnonymous();

        return app;
    }
}
