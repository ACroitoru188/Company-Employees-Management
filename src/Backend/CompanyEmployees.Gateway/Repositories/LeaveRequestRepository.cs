using CompanyEmployees.Domain;
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

        public async Task EnsureDefaultAllocationsAsync(Guid userId, int year)
        {
            var existingTypes = await _context.LeaveAllocations
                .Where(allocation => allocation.UserId == userId && allocation.Year == year)
                .Select(allocation => allocation.LeaveType)
                .ToListAsync();

            var missing = Enum.GetValues<LeaveType>().Except(existingTypes).ToList();
            if (missing.Count == 0)
                return;

            var now = DateTime.UtcNow;
            foreach (var type in missing)
            {
                _context.LeaveAllocations.Add(new LeaveAllocation
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    LeaveType = type,
                    Year = year,
                    NumberOfDays = LeaveAllocationPolicy.DefaultDays(type),
                    CreatedAt = now
                });
            }

            await _context.SaveChangesAsync();
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

        public async Task<List<LeaveRequest>> GetActiveRequestsForUsersAsync(
            List<Guid> userIds, DateOnly from, DateOnly to)
        {
            return await _context.LeaveRequests
                .Include(request => request.User)
                    .ThenInclude(user => user.Department)
                .Where(request => userIds.Contains(request.UserId)
                                  && (request.Status == LeaveStatus.Pending
                                      || request.Status == LeaveStatus.Approved)
                                  && request.StartDate <= to
                                  && request.EndDate >= from)
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
            // Excludes requests this manager has already decided (a Step=ManagerApprovalStep
            // row exists) — those are just waiting on HR now, not on this manager anymore.
            return await _context.LeaveRequests
                .Include(r => r.User)
                    .ThenInclude(u => u.Department)
                .Where(r => r.User.ManagerId == managerId
                            && r.Status == LeaveStatus.Pending
                            && !r.Approvals.Any(a => a.Step == LeaveApproval.ManagerApprovalStep))
                .OrderBy(r => r.StartDate)
                .ToListAsync();
        }

        public async Task<List<LeaveRequest>> GetAllPendingRequestsAsync()
        {
            // Department/Manager are included so LeaveApprovalPolicy can be evaluated per
            // requester (some requesters — HR staff — don't need HR review at all). Approvals
            // excludes requests HR has already decided (a Step=HrApprovalStep row exists) —
            // those are just waiting on the manager now, not on HR anymore.
            var candidates = await _context.LeaveRequests
                .Include(r => r.User)
                    .ThenInclude(u => u.Department)
                .Include(r => r.User)
                    .ThenInclude(u => u.Manager)
                .Where(r => r.Status == LeaveStatus.Pending
                            && !r.Approvals.Any(a => a.Step == LeaveApproval.HrApprovalStep))
                .OrderBy(r => r.StartDate)
                .AsNoTracking()
                .ToListAsync();

            // NeedsHrApproval depends on the requester's role/department/manager triangle,
            // which isn't a single-column filter EF can translate — evaluated client-side.
            return candidates
                .Where(r => LeaveApprovalPolicy.DetermineRequirement(r.User).NeedsHrApproval)
                .ToList();
        }

        public async Task<List<LeaveRequest>> GetAllCompanyPendingRequestsAsync()
        {
            return await _context.LeaveRequests
                .Where(r => r.Status == LeaveStatus.Pending)
                .OrderByDescending(r => r.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<LeaveRequest?> GetRequestByIdAsync(Guid requestId)
        {
            // Manager/Department are included so LeaveApprovalPolicy can be evaluated;
            // Approvals so IsFullyApproved can see whichever side has already decided.
            return await _context.LeaveRequests
                .Include(r => r.User)
                    .ThenInclude(u => u.Manager)
                .Include(r => r.User)
                    .ThenInclude(u => u.Department)
                .Include(r => r.Approvals)
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

        public async Task CancelRequestAsync(LeaveRequest request)
        {
            // "request" is already tracked (it came from GetRequestByIdAsync).
            await _context.SaveChangesAsync();
        }
    }
}
