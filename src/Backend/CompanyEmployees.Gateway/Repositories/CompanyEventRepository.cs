using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class CompanyEventRepository : BaseRepository, ICompanyEventGateway
    {
        public CompanyEventRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task<List<CompanyEvent>> GetAllAsync(CancellationToken ct = default)
        {
            return await _context.CompanyEvents
                .AsNoTracking()
                .OrderBy(e => e.Date.Month)
                .ThenBy(e => e.Date.Day)
                .ToListAsync(ct);
        }

        public async Task<List<CompanyEvent>> GetEventsAsync(string? regionCode, int year, CancellationToken ct = default)
        {
            var all = await _context.CompanyEvents
                .AsNoTracking()
                .ToListAsync(ct);

            var result = new List<CompanyEvent>();
            foreach (var e in all)
            {
                if (!string.IsNullOrEmpty(e.RegionCode)
                    && (regionCode == null || !string.Equals(e.RegionCode, regionCode, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (e.IsAnnual)
                {
                    try
                    {
                        var recurringDate = new DateOnly(year, e.Date.Month, e.Date.Day);
                        result.Add(new CompanyEvent(recurringDate, e.Title, e.Description, e.IsAnnual, e.Category, e.RegionCode, e.Id));
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                }
                else if (e.Date.Year == year)
                {
                    result.Add(e);
                }
            }

            return result.OrderBy(e => e.Date).ToList();
        }

        public async Task<CompanyEvent?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return await _context.CompanyEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        }

        public async Task<CompanyEvent> CreateAsync(CompanyEvent companyEvent, CancellationToken ct = default)
        {
            companyEvent.CreatedAt = DateTime.UtcNow;
            await _context.CompanyEvents.AddAsync(companyEvent, ct);
            await _context.SaveChangesAsync(ct);
            return companyEvent;
        }

        public async Task UpdateAsync(CompanyEvent companyEvent, CancellationToken ct = default)
        {
            companyEvent.UpdatedAt = DateTime.UtcNow;
            _context.CompanyEvents.Update(companyEvent);
            await _context.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var entity = await _context.CompanyEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (entity != null)
            {
                _context.CompanyEvents.Remove(entity);
                await _context.SaveChangesAsync(ct);
            }
        }
    }
}
