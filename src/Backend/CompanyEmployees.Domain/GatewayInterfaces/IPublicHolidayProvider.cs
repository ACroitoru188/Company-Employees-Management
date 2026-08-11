namespace CompanyEmployees.Domain.GatewayInterfaces;

public interface IPublicHolidayProvider
{
    Task<IReadOnlyList<PublicHoliday>> GetHolidaysAsync(
        string countryCode,
        int year,
        CancellationToken cancellationToken = default);
}
