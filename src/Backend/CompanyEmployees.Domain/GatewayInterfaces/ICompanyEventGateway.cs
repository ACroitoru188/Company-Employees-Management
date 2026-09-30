using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces;

public interface ICompanyEventGateway
{
    Task<List<CompanyEvent>> GetAllAsync(CancellationToken ct = default);
    Task<List<CompanyEvent>> GetEventsAsync(string? regionCode, int year, CancellationToken ct = default);
    Task<CompanyEvent?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CompanyEvent> CreateAsync(CompanyEvent companyEvent, CancellationToken ct = default);
    Task UpdateAsync(CompanyEvent companyEvent, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
