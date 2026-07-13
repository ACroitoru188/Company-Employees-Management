using CompanyEmployees.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface IUserSessionGateway
    {
        Task<UserSession> CreateSessionAsync(UserSession session);
        Task<UserSession?> GetSessionByTokenAsync(string token);
        Task<bool> InvalidateSessionAsync(string token);
        Task<bool> InvalidateAllSessionsAsync(Guid userId);
    }
}
