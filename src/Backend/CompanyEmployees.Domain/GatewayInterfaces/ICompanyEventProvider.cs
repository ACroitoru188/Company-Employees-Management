using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces;

public interface ICompanyEventProvider
{
    Task<IReadOnlyList<CompanyEvent>> GetCompanyEventsAsync(
        string? regionCode,
        int year,
        CancellationToken cancellationToken = default);
}
