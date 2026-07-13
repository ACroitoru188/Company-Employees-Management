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
        Task<UserSession> InvalidateSessionAsync();
        Task<UserSession> InvalidateAllSessionsAsync();
    }
}
