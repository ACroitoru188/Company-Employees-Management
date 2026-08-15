using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class DelegatedActionRepository : BaseRepository, IDelegatedActionGateway
    {
        public DelegatedActionRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task CreateAsync(DelegatedAction action)
        {
            await _context.DelegatedActions.AddAsync(action);
            await _context.SaveChangesAsync();
        }

        public Task<List<DelegatedAction>> GetActedAsAsync(Guid actedAsUserId, int skip, int take) =>
            Page(_context.DelegatedActions.Where(a => a.ActedAsUserId == actedAsUserId), skip, take);

        public Task<int> CountActedAsAsync(Guid actedAsUserId) =>
            _context.DelegatedActions.CountAsync(a => a.ActedAsUserId == actedAsUserId);

        public Task<List<DelegatedAction>> GetPerformedByAsync(Guid realUserId, int skip, int take) =>
            Page(_context.DelegatedActions.Where(a => a.RealUserId == realUserId), skip, take);

        public Task<int> CountPerformedByAsync(Guid realUserId) =>
            _context.DelegatedActions.CountAsync(a => a.RealUserId == realUserId);

        public Task<List<DelegatedAction>> GetForRegionAsync(Guid regionId, int skip, int take) =>
            Page(_context.DelegatedActions.Where(a => a.ActedAsUser.RegionId == regionId), skip, take);

        public Task<int> CountForRegionAsync(Guid regionId) =>
            _context.DelegatedActions.CountAsync(a => a.ActedAsUser.RegionId == regionId);

        // Both views render the same three names, so both need the same includes.
        private static Task<List<DelegatedAction>> Page(IQueryable<DelegatedAction> query, int skip, int take) =>
            query
                .Include(a => a.RealUser)
                .Include(a => a.ActedAsUser)
                .Include(a => a.TargetUser)
                .OrderByDescending(a => a.CreatedAt)
                .Skip(skip)
                .Take(take)
                .AsNoTracking()
                .ToListAsync();
    }
}
