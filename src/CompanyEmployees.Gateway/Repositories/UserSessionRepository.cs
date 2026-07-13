using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Gateway.Repositories
{
    public class UserSessionRepository : BaseRepository, IUserSessionGateway
    {
        public UserSessionRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public Task<UserSession> CreateSessionAsync(UserSession session)
        {
            throw new NotImplementedException();
        }

        public async Task<UserSession?> GetSessionByTokenAsync(string token)
        {
            return await _context.UserSessions.
                FirstOrDefaultAsync(s => s.SessionToken == token && s.IsActive);
        }

        public Task<UserSession> InvalidateAllSessionsAsync()
        {
            throw new NotImplementedException();
        }

        public Task<UserSession> InvalidateSessionAsync()
        {
            throw new NotImplementedException();
        }
    }
}
