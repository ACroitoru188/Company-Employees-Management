namespace CompanyEmployees.Domain.GatewayInterfaces;

using CompanyEmployees.Domain.Entities;

public interface ILeaveRequestGateway
{
    Task<List<LeaveRequest>> GetRequestsByUserAsync(Guid userId);

    Task<List<LeaveAllocation>> GetAllocationsByUserAsync(Guid userId, int year);
    Task EnsureDefaultAllocationsAsync(Guid userId, int year);

    Task<List<LeaveRequest>> GetApprovedRequestsForUsersAsync(
        List<Guid> userIds, DateOnly from, DateOnly to);
    Task<List<LeaveRequest>> GetActiveRequestsForUsersAsync(
        List<Guid> userIds, DateOnly from, DateOnly to);

    Task CreateRequestAsync(LeaveRequest request);

    // Pending requests of the manager's direct reports only (User.ManagerId),
    // so the same query serves every level of the hierarchy: PM, LM, Admin.
    Task<List<LeaveRequest>> GetPendingRequestsByManagerAsync(Guid managerId);

    // Every pending request in the company, regardless of manager — the HR
    // dashboard reports org-wide, not per hierarchy branch.
    Task<List<LeaveRequest>> GetAllPendingRequestsAsync();

    // All pending requests across all employees in the company (for Org Chart etc.)
    Task<List<LeaveRequest>> GetAllCompanyPendingRequestsAsync();

    Task<LeaveRequest?> GetRequestByIdAsync(Guid requestId);

    // Persists the request's status change and the approval row atomically.
    Task SaveDecisionAsync(LeaveRequest request, LeaveApproval approval);

    Task UpdateRequestDatesAsync(LeaveRequest request);

    // Persists a self-cancellation. "request" is already tracked (it came from
    // GetRequestByIdAsync), so only the status/reason change needs saving.
    Task CancelRequestAsync(LeaveRequest request);
}
