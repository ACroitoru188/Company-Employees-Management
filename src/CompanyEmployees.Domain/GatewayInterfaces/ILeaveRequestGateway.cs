namespace CompanyEmployees.Domain.GatewayInterfaces;

using CompanyEmployees.Domain.Entities;

public interface ILeaveRequestGateway
{
    Task<List<LeaveRequest>> GetRequestsByUserAsync(Guid userId);

    Task<List<LeaveAllocation>> GetAllocationsByUserAsync(Guid userId, int year);

    Task<List<LeaveRequest>> GetApprovedRequestsForUsersAsync(
        List<Guid> userIds, DateOnly from, DateOnly to);

    Task CreateRequestAsync(LeaveRequest request);

    // Pending requests of the manager's direct reports only (User.ManagerId),
    // so the same query serves every level of the hierarchy: PM, LM, Admin.
    Task<List<LeaveRequest>> GetPendingRequestsByManagerAsync(Guid managerId);

    Task<LeaveRequest?> GetRequestByIdAsync(Guid requestId);

    // Persists the request's status change and the approval row atomically.
    Task SaveDecisionAsync(LeaveRequest request, LeaveApproval approval);
}
