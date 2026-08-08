using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface IDelegatedActionGateway
    {
        Task CreateAsync(DelegatedAction action);

        // What was done in this account's name, by whoever was covering for it.
        Task<List<DelegatedAction>> GetActedAsAsync(Guid actedAsUserId, int skip, int take);
        Task<int> CountActedAsAsync(Guid actedAsUserId);

        // What this person did while covering for someone else.
        Task<List<DelegatedAction>> GetPerformedByAsync(Guid realUserId, int skip, int take);
        Task<int> CountPerformedByAsync(Guid realUserId);
    }
}
