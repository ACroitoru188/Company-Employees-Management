using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface IContractGateway
    {
        Task<Contract?> GetByIdAsync(Guid contractId);
        Task<Contract?> GetActiveContractByUserIdAsync(Guid userId);
        Task<List<Contract>> GetContractsByUserIdAsync(Guid userId);
        Task<List<Contract>> GetAllContractsAsync();
        Task CreateAsync(Contract contract);
        Task UpdateAsync(Contract contract);
    }
}
