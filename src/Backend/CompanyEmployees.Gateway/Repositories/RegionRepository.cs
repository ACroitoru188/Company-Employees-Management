using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories;

public sealed class RegionRepository : BaseRepository, IRegionGateway
{
    public RegionRepository(CompanyEmployeesDbContext context) : base(context)
    {
    }

    public Task<List<Region>> GetAllAsync(bool activeOnly = false)
    {
        var query = _context.Regions.AsNoTracking();
        if (activeOnly)
            query = query.Where(region => region.IsActive);

        return query.OrderBy(region => region.Name).ToListAsync();
    }

    public Task<Region?> GetByIdAsync(Guid id) =>
        _context.Regions.AsNoTracking().FirstOrDefaultAsync(region => region.Id == id);
}
