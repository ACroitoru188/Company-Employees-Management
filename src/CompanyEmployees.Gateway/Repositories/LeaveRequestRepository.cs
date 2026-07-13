using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class LeaveRequestRepository : BaseRepository, ILeaveRequestGateway
    {
        public LeaveRequestRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task<List<LeaveRequest>> GetRequestsByUserAsync(Guid userId)
        {
            return await _context.LeaveRequests
                .Include(r => r.Approvals)
                    .ThenInclude(a => a.Approver)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<LeaveAllocation>> GetAllocationsByUserAsync(Guid userId, int year)
        {
            return await _context.LeaveAllocations
                .Where(a => a.UserId == userId && a.Year == year)
                .ToListAsync();
        }

        public async Task<List<LeaveRequest>> GetApprovedRequestsForUsersAsync(
            List<Guid> userIds, DateOnly from, DateOnly to)
        {
            // Two ranges overlap when each one starts before the other ends.
            return await _context.LeaveRequests
                .Include(r => r.User)
                .Where(r => userIds.Contains(r.UserId)
                            && r.Status == LeaveStatus.Approved
                            && r.StartDate <= to
                            && r.EndDate >= from)
                .ToListAsync();
        }

        public async Task CreateRequestAsync(LeaveRequest request)
        {
            await _context.LeaveRequests.AddAsync(request);
            await _context.SaveChangesAsync();
        }
    }
}
