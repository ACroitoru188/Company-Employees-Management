using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class ManagerDelegationRepository : BaseRepository, IManagerDelegationGateway
    {
        public ManagerDelegationRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task<ManagerDelegation?> GetByIdAsync(Guid delegationId)
        {
            return await _context.ManagerDelegations
                .Include(md => md.Manager)
                .Include(md => md.Delegate)
                .FirstOrDefaultAsync(md => md.Id == delegationId);
        }

        public async Task<List<ManagerDelegation>> GetActiveDelegationsForManagerAsync(Guid managerId, DateOnly onDate)
        {
            return await _context.ManagerDelegations
                .Include(md => md.Delegate)
                .Where(md => md.ManagerId == managerId && md.IsActive && md.StartDate <= onDate && md.EndDate >= onDate)
                .OrderBy(md => md.StartDate)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<ManagerDelegation>> GetActiveDelegationsForDelegateAsync(Guid delegateId, DateOnly onDate)
        {
            return await _context.ManagerDelegations
                .Include(md => md.Manager)
                .Where(md => md.DelegateId == delegateId && md.IsActive && md.StartDate <= onDate && md.EndDate >= onDate)
                .OrderBy(md => md.StartDate)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<ManagerDelegation>> GetAllDelegationsByManagerAsync(Guid managerId)
        {
            return await _context.ManagerDelegations
                .Include(md => md.Delegate)
                .Where(md => md.ManagerId == managerId)
                .OrderByDescending(md => md.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Guid>> GetDelegatedManagerIdsAsync(Guid delegateId, DateOnly onDate)
        {
            return await _context.ManagerDelegations
                .Where(md => md.DelegateId == delegateId && md.IsActive && md.StartDate <= onDate && md.EndDate >= onDate)
                .Select(md => md.ManagerId)
                .Distinct()
                .ToListAsync();
        }

        public Task<bool> HasActiveDelegationInPeriodAsync(Guid managerId, DateOnly from, DateOnly to) =>
            _context.ManagerDelegations.AnyAsync(md =>
                md.ManagerId == managerId && md.IsActive && md.StartDate <= to && md.EndDate >= from);

        public Task<bool> HasAnyDelegationAsync(Guid userId) =>
            _context.ManagerDelegations.AnyAsync(md =>
                md.ManagerId == userId || md.DelegateId == userId);

        public async Task CreateAsync(ManagerDelegation delegation)
        {
            await _context.ManagerDelegations.AddAsync(delegation);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(ManagerDelegation delegation)
        {
            _context.ManagerDelegations.Update(delegation);
            await _context.SaveChangesAsync();
        }
    }
}
