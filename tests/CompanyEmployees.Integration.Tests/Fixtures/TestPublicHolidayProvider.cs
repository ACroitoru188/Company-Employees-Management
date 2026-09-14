using CompanyEmployees.Domain;
using CompanyEmployees.Domain.GatewayInterfaces;

namespace CompanyEmployees.Integration.Tests.Fixtures;

public class TestPublicHolidayProvider : IPublicHolidayProvider
{
    public Task<IReadOnlyList<PublicHoliday>> GetHolidaysAsync(
        string countryCode,
        int year,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PublicHoliday>>(Array.Empty<PublicHoliday>());
    }
}
