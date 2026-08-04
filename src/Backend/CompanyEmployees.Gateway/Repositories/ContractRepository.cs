using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class ContractRepository : BaseRepository, IContractGateway
    {
        public ContractRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task<Contract?> GetByIdAsync(Guid contractId)
        {
            return await _context.Contracts
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == contractId);
        }

        public async Task<Contract?> GetActiveContractByUserIdAsync(Guid userId)
        {
            return await _context.Contracts
                .Where(c => c.UserId == userId && c.Status == ContractStatus.Active)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<List<Contract>> GetContractsByUserIdAsync(Guid userId)
        {
            return await _context.Contracts
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Contract>> GetAllContractsAsync()
        {
            return await _context.Contracts
                .Include(c => c.User)
                .OrderByDescending(c => c.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task CreateAsync(Contract contract)
        {
            await _context.Contracts.AddAsync(contract);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Contract contract)
        {
            _context.Contracts.Update(contract);
            await _context.SaveChangesAsync();
        }
    }
}
