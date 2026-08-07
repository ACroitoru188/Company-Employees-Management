using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using CompanyEmployees.Domain;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Infrastructure.Holidays;

public sealed class NagerDatePublicHolidayProvider : IPublicHolidayProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NagerDatePublicHolidayProvider> _logger;
    private static readonly ConcurrentDictionary<(string CountryCode, int Year), IReadOnlyList<PublicHoliday>> Cache = new();

    public NagerDatePublicHolidayProvider(
        HttpClient httpClient,
        ILogger<NagerDatePublicHolidayProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PublicHoliday>> GetHolidaysAsync(
        string countryCode,
        int year,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = countryCode.Trim().ToUpperInvariant();
        var key = (normalizedCode, year);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            using var response = await _httpClient.GetAsync(
                $"publicholidays/{year}/{Uri.EscapeDataString(normalizedCode)}",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var fallback = FallbackHolidays(normalizedCode, year);
                _logger.LogWarning("Using the built-in public-holiday calendar for {CountryCode} in {Year}.", normalizedCode, year);
                Cache[key] = fallback;
                return fallback;
            }

            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<List<NagerHoliday>>(cancellationToken: cancellationToken) ?? [];
            var holidays = rows
                .Where(row => row.Global)
                .Select(row => new PublicHoliday(row.Date, row.LocalName ?? row.Name ?? "Public holiday"))
                .DistinctBy(holiday => holiday.Date)
                .OrderBy(holiday => holiday.Date)
                .ToList();

            Cache[key] = holidays;
            return holidays;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Public holidays could not be loaded for {CountryCode} in {Year}.", normalizedCode, year);
            return FallbackHolidays(normalizedCode, year);
        }
    }

    private static IReadOnlyList<PublicHoliday> FallbackHolidays(string countryCode, int year)
    {
        var holidays = countryCode switch
        {
            "PK" => new List<PublicHoliday>
            {
                Holiday(year, 2, 5, "Kashmir Solidarity Day"),
                Holiday(year, 3, 23, "Pakistan Day"),
                Holiday(year, 5, 1, "Labour Day"),
                Holiday(year, 8, 14, "Independence Day"),
                Holiday(year, 11, 9, "Iqbal Day"),
                Holiday(year, 12, 25, "Quaid-e-Azam Day")
            },
            "IN" when year == 2026 => new List<PublicHoliday>
            {
                Holiday(year, 1, 26, "Republic Day"),
                Holiday(year, 3, 4, "Holi"),
                Holiday(year, 3, 21, "Id-ul-Fitr"),
                Holiday(year, 3, 26, "Ram Navami"),
                Holiday(year, 3, 31, "Mahavir Jayanti"),
                Holiday(year, 4, 3, "Good Friday"),
                Holiday(year, 5, 1, "Buddha Purnima"),
                Holiday(year, 5, 27, "Id-ul-Zuha"),
                Holiday(year, 6, 26, "Muharram"),
                Holiday(year, 8, 15, "Independence Day"),
                Holiday(year, 8, 26, "Milad-un-Nabi"),
                Holiday(year, 9, 4, "Janmashtami"),
                Holiday(year, 10, 2, "Mahatma Gandhi Jayanti"),
                Holiday(year, 10, 20, "Dussehra"),
                Holiday(year, 11, 8, "Diwali"),
                Holiday(year, 11, 24, "Guru Nanak's Birthday"),
                Holiday(year, 12, 25, "Christmas Day")
            },
            "IN" => new List<PublicHoliday>
            {
                Holiday(year, 1, 26, "Republic Day"),
                Holiday(year, 8, 15, "Independence Day"),
                Holiday(year, 10, 2, "Mahatma Gandhi Jayanti")
            },
            "AE" => new List<PublicHoliday>
            {
                Holiday(year, 1, 1, "Gregorian New Year"),
                Holiday(year, 12, 2, "UAE National Day"),
                Holiday(year, 12, 3, "UAE National Day Holiday")
            },
            _ => []
        };

        return holidays.OrderBy(holiday => holiday.Date).ToList();
    }

    private static PublicHoliday Holiday(int year, int month, int day, string name) =>
        new(new DateOnly(year, month, day), name);

    private sealed record NagerHoliday(DateOnly Date, string? LocalName, string? Name, bool Global);
}
