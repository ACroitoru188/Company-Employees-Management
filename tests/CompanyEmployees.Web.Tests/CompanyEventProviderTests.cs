using CompanyEmployees.Domain;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Infrastructure.Events;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CompanyEmployees.Web.Tests;

public class CompanyEventProviderTests
{
    [Fact]
    public async Task GetCompanyEventsAsync_returns_default_annual_events_for_year()
    {
        var provider = new CompanyEventProvider();

        var events = await provider.GetCompanyEventsAsync(regionCode: "RO", year: 2026);

        Assert.NotEmpty(events);
        var anniversary = events.FirstOrDefault(e => e.Title.Contains("Anniversary", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(anniversary);
        Assert.Equal(new DateOnly(2026, 10, 12), anniversary.Date);
        Assert.True(anniversary.IsAnnual);
    }

    [Fact]
    public async Task GetCompanyEventsAsync_includes_configured_custom_events()
    {
        var configData = new Dictionary<string, string?>
        {
            ["CompanyEvents:0:Title"] = "Summer Family Day",
            ["CompanyEvents:0:Month"] = "7",
            ["CompanyEvents:0:Day"] = "18",
            ["CompanyEvents:0:IsAnnual"] = "true",
            ["CompanyEvents:0:Category"] = "Social"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var provider = new CompanyEventProvider(configuration);

        var events = await provider.GetCompanyEventsAsync(regionCode: "RO", year: 2026);

        var custom = events.FirstOrDefault(e => e.Title == "Summer Family Day");
        Assert.NotNull(custom);
        Assert.Equal(new DateOnly(2026, 7, 18), custom.Date);
    }
}
