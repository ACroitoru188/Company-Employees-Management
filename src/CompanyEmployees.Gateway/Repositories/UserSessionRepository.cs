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

        public async Task<UserSession> CreateSessionAsync(UserSession session)
        {
            _context.UserSessions.Add(session);
            await _context.SaveChangesAsync();
            return session;
        }

        public async Task<UserSession?> GetSessionByTokenAsync(string token)
        {
            return await _context.UserSessions.
                FirstOrDefaultAsync(s => s.SessionToken == token && s.IsActive);
        }

        public async Task<bool> InvalidateAllSessionsAsync(Guid userId)
        {
            var sessions = await _context.UserSessions
                .Where(s => s.UserId == userId && s.IsActive)
                .ToListAsync();

            if (!sessions.Any()) return false;

            foreach (var session in sessions)
            {
                session.IsActive = false;
            }
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> InvalidateSessionAsync(string token)
        {
            var session = await _context.UserSessions
                .FirstOrDefaultAsync(s => s.SessionToken == token && s.IsActive);

            if (session == null) return false;

            session.IsActive = false;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
