using System.Text.Json;

namespace CompanyEmployees.Web.Setup;

public sealed record ProviderStartupMetrics(
    string ProviderId,
    string DisplayName,
    long TotalStartupMs,
    long FailoverSelectMs,
    long MigrationsMs,
    long SeedingMs,
    long StandbyBootstrapMs,
    DateTime RecordedUtc);

public sealed record ProviderComparisonAnalysis(
    string? FasterMigrationsProvider,
    long MigrationsDifferenceMs,
    double MigrationsPercentFaster,
    string? FasterStartupProvider,
    long StartupDifferenceMs,
    double StartupPercentFaster,
    string Summary);

public sealed record ProviderComparisonReport(
    Dictionary<string, ProviderStartupMetrics> Providers,
    ProviderComparisonAnalysis? Analysis,
    DateTime LastUpdatedUtc);

public sealed class ProviderBenchmarkStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private ProviderComparisonReport? _cachedReport;
    private readonly object _lock = new();

    public ProviderBenchmarkStore(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "provider-benchmarks.json");
        _cachedReport = LoadFromDisk();
    }

    public ProviderComparisonReport GetReport()
    {
        if (_cachedReport != null)
            return _cachedReport;

        lock (_lock)
        {
            _cachedReport ??= LoadFromDisk();
            return _cachedReport;
        }
    }

    public ProviderComparisonReport Load() => GetReport();

    private ProviderComparisonReport LoadFromDisk()
    {
        if (!File.Exists(_filePath))
        {
            return new ProviderComparisonReport(
                new Dictionary<string, ProviderStartupMetrics>(StringComparer.OrdinalIgnoreCase),
                null,
                DateTime.UtcNow);
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var report = JsonSerializer.Deserialize<ProviderComparisonReport>(json, JsonOptions);
            return report ?? new ProviderComparisonReport(
                new Dictionary<string, ProviderStartupMetrics>(StringComparer.OrdinalIgnoreCase),
                null,
                DateTime.UtcNow);
        }
        catch
        {
            return new ProviderComparisonReport(
                new Dictionary<string, ProviderStartupMetrics>(StringComparer.OrdinalIgnoreCase),
                null,
                DateTime.UtcNow);
        }
    }

    public async Task RecordStartupAsync(
        string providerId,
        string displayName,
        long totalStartupMs,
        long failoverSelectMs,
        long migrationsMs,
        long seedingMs,
        long standbyBootstrapMs,
        CancellationToken ct = default)
    {
        var current = Load();
        var providers = new Dictionary<string, ProviderStartupMetrics>(current.Providers, StringComparer.OrdinalIgnoreCase);

        providers[providerId] = new ProviderStartupMetrics(
            ProviderId: providerId,
            DisplayName: displayName,
            TotalStartupMs: totalStartupMs,
            FailoverSelectMs: failoverSelectMs,
            MigrationsMs: migrationsMs,
            SeedingMs: seedingMs,
            StandbyBootstrapMs: standbyBootstrapMs,
            RecordedUtc: DateTime.UtcNow);

        var analysis = GenerateAnalysis(providers);
        var updated = new ProviderComparisonReport(providers, analysis, DateTime.UtcNow);
        _cachedReport = updated;

        var json = JsonSerializer.Serialize(updated, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    public ProviderComparisonAnalysis? GenerateAnalysis(Dictionary<string, ProviderStartupMetrics> providers)
    {
        if (providers.Count < 2)
            return null;

        var list = providers.Values.OrderByDescending(p => p.RecordedUtc).ToList();
        var p1 = list[0];
        var p2 = list[1];

        // Compare migrations
        string? fasterMig = null;
        long migDiff = 0;
        double migPct = 0;
        if (p1.MigrationsMs != p2.MigrationsMs)
        {
            var faster = p1.MigrationsMs < p2.MigrationsMs ? p1 : p2;
            var slower = p1.MigrationsMs < p2.MigrationsMs ? p2 : p1;
            fasterMig = faster.DisplayName;
            migDiff = slower.MigrationsMs - faster.MigrationsMs;
            migPct = slower.MigrationsMs > 0 ? Math.Round((double)migDiff / slower.MigrationsMs * 100, 1) : 0;
        }

        // Compare total boot
        string? fasterStartup = null;
        long bootDiff = 0;
        double bootPct = 0;
        if (p1.TotalStartupMs != p2.TotalStartupMs)
        {
            var faster = p1.TotalStartupMs < p2.TotalStartupMs ? p1 : p2;
            var slower = p1.TotalStartupMs < p2.TotalStartupMs ? p2 : p1;
            fasterStartup = faster.DisplayName;
            bootDiff = slower.TotalStartupMs - faster.TotalStartupMs;
            bootPct = slower.TotalStartupMs > 0 ? Math.Round((double)bootDiff / slower.TotalStartupMs * 100, 1) : 0;
        }

        var migSummary = fasterMig != null
            ? $"{fasterMig} migrated faster by {migDiff}ms ({migPct}%)."
            : $"Both had identical migration times ({p1.MigrationsMs}ms).";

        var bootSummary = fasterStartup != null
            ? $"{fasterStartup} booted faster by {bootDiff}ms ({bootPct}%)."
            : $"Both had identical startup times ({p1.TotalStartupMs}ms).";

        var summary = $"{migSummary} {bootSummary}";

        return new ProviderComparisonAnalysis(
            FasterMigrationsProvider: fasterMig,
            MigrationsDifferenceMs: migDiff,
            MigrationsPercentFaster: migPct,
            FasterStartupProvider: fasterStartup,
            StartupDifferenceMs: bootDiff,
            StartupPercentFaster: bootPct,
            Summary: summary);
    }
}
