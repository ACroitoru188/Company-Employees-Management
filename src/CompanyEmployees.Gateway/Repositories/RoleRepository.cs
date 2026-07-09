using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class RoleRepository : BaseRepository, IRoleGateway
    {
        public RoleRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task<List<Role>> GetAllRolesAsync()
        {
            return await _context.Roles
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Role?> GetRoleByIdAsync(int roleId)
        {
            return await _context.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RoleId == roleId);
        }
    }
}
