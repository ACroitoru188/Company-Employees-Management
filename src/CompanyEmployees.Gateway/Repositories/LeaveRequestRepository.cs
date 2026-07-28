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
                    .ThenInclude(u => u.Department)
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

        public async Task<List<LeaveRequest>> GetPendingRequestsByManagerAsync(Guid managerId)
        {
            // Department is included so the manager list can show where each requester sits.
            return await _context.LeaveRequests
                .Include(r => r.User)
                    .ThenInclude(u => u.Department)
                .Where(r => r.User.ManagerId == managerId && r.Status == LeaveStatus.Pending)
                .OrderBy(r => r.StartDate)
                .ToListAsync();
        }

        public async Task<List<LeaveRequest>> GetAllPendingRequestsAsync()
        {
            // Department is included so the HR list can show where each requester sits.
            return await _context.LeaveRequests
                .Include(r => r.User)
                    .ThenInclude(u => u.Department)
                .Where(r => r.Status == LeaveStatus.Pending)
                .OrderBy(r => r.StartDate)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<LeaveRequest?> GetRequestByIdAsync(Guid requestId)
        {
            // User is included because the decision flow needs the requester's ManagerId.
            return await _context.LeaveRequests
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == requestId);
        }

        public async Task SaveDecisionAsync(LeaveRequest request, LeaveApproval approval)
        {
            // "request" is already tracked (it came from GetRequestByIdAsync), so its
            // status change and the new approval land in the same SaveChanges — one
            // transaction; a crash can't leave the status updated without its approval.
            _context.LeaveApprovals.Add(approval);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateRequestDatesAsync(LeaveRequest request)
        {
            // "request" is already tracked (it came from GetRequestByIdAsync).
            await _context.SaveChangesAsync();
        }
    }
}
