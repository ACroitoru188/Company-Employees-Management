using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class UserRepository : BaseRepository, IUserGateway
    {
        public UserRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task<User?> GetUserByIdAsync(Guid userId)
        {
            return await _context.Users
                .Include(u => u.Manager)
                .Include(u => u.Department)
                .Include(u => u.Region)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.Region)
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.Region)
                .Include(u => u.Contracts)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<User>> GetDirectReportsAsync(Guid managerId)
        {
            // Inactive users are soft-deleted, so they must not count towards a team.
            return await _context.Users
                .Include(u => u.Department)
                .Include(u => u.Region)
                .Where(u => u.ManagerId == managerId && u.Status == UserStatus.Active)
                .OrderBy(u => u.Name)
                .AsNoTracking()
                .ToListAsync();
        }

        public Task<List<User>> GetAllDirectReportsAsync(Guid managerId) =>
            _context.Users
                .Include(user => user.Region)
                .Where(user => user.ManagerId == managerId)
                .ToListAsync();

        public async Task CreateUserAsync(User user)
        {
            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateUserAsync(User user)
        {
            if (_context.Entry(user).State == EntityState.Detached)
            {
                _context.Users.Update(user);
            }
            await _context.SaveChangesAsync();
        }

        public async Task DeleteUserAsync(Guid userId)
        {
            var user = await GetUserByIdAsync(userId);
            if (user != null)
            {
                // Soft delete: mirror the old Employee.IsActive behaviour via Status.
                user.Status = UserStatus.Inactive;
                await _context.SaveChangesAsync();
            }
        }
    }
}
