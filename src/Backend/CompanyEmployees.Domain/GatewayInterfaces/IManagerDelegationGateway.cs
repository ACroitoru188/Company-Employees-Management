using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface IManagerDelegationGateway
    {
        Task<ManagerDelegation?> GetByIdAsync(Guid delegationId);
        Task<List<ManagerDelegation>> GetActiveDelegationsForManagerAsync(Guid managerId, DateOnly onDate);
        Task<List<ManagerDelegation>> GetActiveDelegationsForDelegateAsync(Guid delegateId, DateOnly onDate);
        Task<List<ManagerDelegation>> GetAllDelegationsByManagerAsync(Guid managerId);
        Task<List<Guid>> GetDelegatedManagerIdsAsync(Guid delegateId, DateOnly onDate);
        Task CreateAsync(ManagerDelegation delegation);
        Task UpdateAsync(ManagerDelegation delegation);
    }
}
