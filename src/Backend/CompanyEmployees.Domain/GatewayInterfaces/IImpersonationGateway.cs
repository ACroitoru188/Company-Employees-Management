using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface IImpersonationGateway
    {
        Task CreateAsync(ImpersonationSession session);

        // The session a user is currently inside, if any. Used to close it on the way out
        // and to refuse a second one.
        Task<ImpersonationSession?> GetOpenSessionAsync(Guid realUserId);

        Task EndSessionAsync(Guid sessionId, DateTime endedAt);
    }
}
