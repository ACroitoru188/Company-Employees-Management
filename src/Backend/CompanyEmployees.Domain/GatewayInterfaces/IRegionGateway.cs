using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces;

public interface IRegionGateway
{
    Task<List<Region>> GetAllAsync(bool activeOnly = false);
    Task<Region?> GetByIdAsync(Guid id);
}
