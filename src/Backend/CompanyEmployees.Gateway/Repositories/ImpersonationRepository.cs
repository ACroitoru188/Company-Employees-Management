using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class ImpersonationRepository : BaseRepository, IImpersonationGateway
    {
        public ImpersonationRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task CreateAsync(ImpersonationSession session)
        {
            await _context.ImpersonationSessions.AddAsync(session);
            await _context.SaveChangesAsync();
        }

        public async Task<ImpersonationSession?> GetOpenSessionAsync(Guid realUserId)
        {
            return await _context.ImpersonationSessions
                .Include(s => s.Delegation)
                .Where(s => s.RealUserId == realUserId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();
        }

        public Task EndSessionAsync(Guid sessionId, DateTime endedAt) =>
            _context.ImpersonationSessions
                .Where(s => s.Id == sessionId && s.EndedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.EndedAt, endedAt));
    }
}
