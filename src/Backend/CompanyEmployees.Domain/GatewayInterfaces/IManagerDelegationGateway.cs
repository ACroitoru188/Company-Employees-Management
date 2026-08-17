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

        // Overlap, not full coverage: a manager is allowed to hand over a shorter stretch
        // than the leave they are taking.
        Task<bool> HasActiveDelegationInPeriodAsync(Guid managerId, DateOnly from, DateOnly to);

        // Ever given or received one, active or not — decides whether the history entry is
        // worth showing in the nav at all.
        Task<bool> HasAnyDelegationAsync(Guid userId);
        Task CreateAsync(ManagerDelegation delegation);
        Task UpdateAsync(ManagerDelegation delegation);
    }
}
