using CompanyEmployees.Domain;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
// The domain defines its own InvalidOperationException; the alias picks it over System's.
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Contexts
{
    public class ManagerContext : BaseContext
    {
        private readonly ILeaveRequestGateway _leaveRequestGateway;
        private readonly IUserGateway _userGateway;
        private readonly NotificationContext _notifications;

        public ManagerContext(
            ILogger<ManagerContext> logger,
            ILeaveRequestGateway leaveRequestGateway,
            IUserGateway userGateway,
            NotificationContext notifications) : base(logger)
        {
            _leaveRequestGateway = leaveRequestGateway;
            _userGateway = userGateway;
            _notifications = notifications;
        }

        public Task<List<LeaveRequest>> GetPendingRequestsForManagerAsync(Guid managerId)
        {
            return _leaveRequestGateway.GetPendingRequestsByManagerAsync(managerId);
        }

        // Scoped to the manager's own direct reports. This is deliberately not
        // EmployeeContext.GetTeamMembersAsync, which answers "who shares my manager"
        // (peers) rather than "who reports to me".
        public async Task<ManagerDashboardResult> GetManagerDashboardAsync(Guid managerId)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var result = new ManagerDashboardResult();

            var reports = await _userGateway.GetDirectReportsAsync(managerId);
            result.TeamSize = reports.Count;

            if (reports.Count == 0)
                return result;

            var reportIds = new List<Guid>();
            foreach (var person in reports)
            {
                reportIds.Add(person.Id);
            }

            var onLeave = await _leaveRequestGateway
                .GetApprovedRequestsForUsersAsync(reportIds, today, today);

            // Count people, not requests: two overlapping approved requests are still
            // one person away.
            var outToday = new HashSet<Guid>();
            foreach (var request in onLeave)
            {
                outToday.Add(request.UserId);
            }
            result.OnLeaveToday = outToday.Count;

            foreach (var person in reports)
            {
                result.Team.Add(new ManagerTeamMember
                {
                    Id = person.Id,
                    Name = person.Name,
                    Role = person.Role.ToString(),
                    Department = person.Department == null ? "—" : person.Department.Name,
                    OnLeaveToday = outToday.Contains(person.Id)
                });
            }

            var pending = await _leaveRequestGateway.GetPendingRequestsByManagerAsync(managerId);
            result.PendingRequests = pending.Count;

            foreach (var request in pending)
            {
                // Same 7-day threshold as the HR dashboard, so the two agree.
                var waiting = (DateTime.UtcNow - request.CreatedAt).Days;
                if (waiting > 7)
                    result.StaleRequests++;

                result.Pending.Add(new ManagerPendingRequest
                {
                    RequestId = request.Id,
                    Name = request.User.Name,
                    Department = request.User.Department == null ? "—" : request.User.Department.Name,
                    Type = request.Type.ToString(),
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    Days = request.EndDate.DayNumber - request.StartDate.DayNumber + 1,
                    WaitingDays = waiting
                });
            }

            return result;
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

            if (request.Approvals.Any(a => a.Step == LeaveApproval.ManagerApprovalStep))
                throw new InvalidOperationException("You have already decided this request.");

            var requirement = LeaveApprovalPolicy.DetermineRequirement(request.User);

            var approval = new LeaveApproval
            {
                LeaveRequestId = request.Id,
                ApproverId = managerId,
                Step = LeaveApproval.ManagerApprovalStep,
                Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected,
                ReviewedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            request.Approvals.Add(approval);

            // A reject is final immediately — no reason to make HR review a doomed request.
            // An approve only finalizes once every required approver (HR, if this request
            // needs it) has also approved; otherwise the request stays Pending.
            var isFinal = !approve || LeaveApprovalPolicy.IsFullyApproved(request, requirement);
            if (isFinal)
                request.Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;

            await _leaveRequestGateway.SaveDecisionAsync(request, approval);

            if (isFinal)
            {
                // The decision is already committed; a notification failure must not undo it.
                try
                {
                    var period = request.StartDate.ToString("MMM d", CultureInfo.InvariantCulture)
                                 + " – " +
                                 request.EndDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
                    await _notifications.SendNotificationAsync(
                        request.UserId,
                        $"Your {request.Type} leave request for {period} was {(request.Status == LeaveStatus.Approved ? "approved" : "declined")}.",
                        "/employee/my-requests");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Decision on {RequestId} saved but the notification failed.", requestId);
                }
            }

            _logger.LogInformation("Manager {ManagerId} {Decision} leave request {RequestId}{Final}.",
                managerId, approve ? "approved" : "rejected", requestId, isFinal ? "" : " (still awaiting HR)");
            return request;
        }
    }
}
