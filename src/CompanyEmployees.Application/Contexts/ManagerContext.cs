using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
// The domain defines its own InvalidOperationException; the alias picks it over System's.
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Contexts
{
    public class ManagerContext : BaseContext
    {
        private readonly ILeaveRequestGateway _leaveRequestGateway;

        public ManagerContext(
            ILogger<ManagerContext> logger,
            ILeaveRequestGateway leaveRequestGateway) : base(logger)
        {
            _leaveRequestGateway = leaveRequestGateway;
        }

        public Task<List<LeaveRequest>> GetPendingRequestsForManagerAsync(Guid managerId)
        {
            return _leaveRequestGateway.GetPendingRequestsByManagerAsync(managerId);
        }

        public async Task<LeaveRequest> DecideRequestAsync(Guid managerId, Guid requestId, bool approve)
        {
            var request = await _leaveRequestGateway.GetRequestByIdAsync(requestId);
            if (request == null)
                throw new EntityNotFoundException($"No leave request with id {requestId}.");

            if (request.Status != LeaveStatus.Pending)
                throw new InvalidOperationException("This request has already been decided.");

            // [Authorize] on the page only proves the caller is *a* manager; being the
            // requester's own manager must be enforced here, where the UI can't bypass it.
            if (request.User.ManagerId != managerId)
                throw new UnauthorizedException("You are not this employee's manager.");

            request.Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;

            var approval = new LeaveApproval
            {
                LeaveRequestId = request.Id,
                ApproverId = managerId,
                Step = 1,
                Status = request.Status,
                ReviewedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await _leaveRequestGateway.SaveDecisionAsync(request, approval);

            _logger.LogInformation("Manager {ManagerId} {Decision} leave request {RequestId}.",
                managerId, approve ? "approved" : "rejected", requestId);
            return request;
        }
    }
}
