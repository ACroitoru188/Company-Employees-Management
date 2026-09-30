using CompanyEmployees.Domain;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Infrastructure.Events;

public sealed class CompanyEventProvider : ICompanyEventProvider
{
    private readonly IConfiguration? _configuration;
    private readonly ILogger<CompanyEventProvider>? _logger;

    // Recurring annual events (Month, Day, Title, Description, Category)
    private static readonly (int Month, int Day, string Title, string? Description, string Category)[] DefaultAnnualEvents =
    [
        (4, 7, "Health & Wellbeing Day", "Company-wide focus on mental and physical wellness", "Wellbeing"),
        (6, 5, "Sustainability Day", "Global sustainability workshops and green initiatives", "Sustainability"),
        (10, 12, "Siemens Anniversary", "Celebrating the founding of Siemens on October 12, 1847", "Anniversary"),
        (11, 14, "Innovation & Tech Day", "Showcasing breakthroughs, engineering patents, and AI solutions", "Innovation"),
        (12, 18, "Annual All-Hands & Town Hall", "Global company review, milestones, and future vision", "TownHall")
    ];

    public CompanyEventProvider(
        IConfiguration? configuration = null,
        ILogger<CompanyEventProvider>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task<IReadOnlyList<CompanyEvent>> GetCompanyEventsAsync(
        string? regionCode,
        int year,
        CancellationToken cancellationToken = default)
    {
        var result = new List<CompanyEvent>();

        // 1. Add default annual corporate events for the requested year
        foreach (var (month, day, title, description, category) in DefaultAnnualEvents)
        {
            try
            {
                var eventDate = new DateOnly(year, month, day);
                result.Add(new CompanyEvent(eventDate, title, description, isAnnual: true, category: category));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid date for year (e.g. leap year mismatch), skip
            }
        }

        // 2. Read optional custom events from appsettings "CompanyEvents" section
        if (_configuration != null)
        {
            var customSection = _configuration.GetSection("CompanyEvents");
            if (customSection.Exists())
            {
                foreach (var child in customSection.GetChildren())
                {
                    var title = child["Title"];
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    var description = child["Description"];
                    var category = child["Category"] ?? "General";
                    var eventRegion = child["RegionCode"];
                    var isAnnual = bool.TryParse(child["IsAnnual"], out var annual) && annual;

                    // Specific date e.g. "2026-10-15"
                    if (DateOnly.TryParse(child["Date"], out var specificDate))
                    {
                        if (specificDate.Year == year)
                        {
                            result.Add(new CompanyEvent(specificDate, title, description, isAnnual, category, eventRegion));
                        }
                    }
                    else if (int.TryParse(child["Month"], out var month) && int.TryParse(child["Day"], out var day))
                    {
                        try
                        {
                            var recurringDate = new DateOnly(year, month, day);
                            result.Add(new CompanyEvent(recurringDate, title, description, isAnnual, category, eventRegion));
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                        }
                    }
                }
            }
        }

        // 3. Filter by region (null = global event, otherwise match user's region)
        var filtered = result
            .Where(e => string.IsNullOrEmpty(e.RegionCode)
                     || (regionCode != null && string.Equals(e.RegionCode, regionCode, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(e => (e.Date, e.Title))
            .OrderBy(e => e.Date)
            .ToList();

        return Task.FromResult<IReadOnlyList<CompanyEvent>>(filtered);
    }
}
