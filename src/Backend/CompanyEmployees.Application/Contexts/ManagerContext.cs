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
        private readonly IContractGateway _contractGateway;
        private readonly IManagerDelegationGateway _delegationGateway;
        private readonly NotificationContext _notifications;
        private readonly IDelegatedActionGateway _delegatedActions;
        private readonly DelegationGuard _delegationGuard;

        public ManagerContext(
            ILogger<ManagerContext> logger,
            ILeaveRequestGateway leaveRequestGateway,
            IUserGateway userGateway,
            IContractGateway contractGateway,
            IManagerDelegationGateway delegationGateway,
            IPublicHolidayProvider holidayProvider,
            NotificationContext notifications,
            IDelegatedActionGateway delegatedActions,
            DelegationGuard delegationGuard) : base(logger, holidayProvider)
        {
            _leaveRequestGateway = leaveRequestGateway;
            _userGateway = userGateway;
            _contractGateway = contractGateway;
            _delegationGateway = delegationGateway;
            _notifications = notifications;
            _delegatedActions = delegatedActions;
            _delegationGuard = delegationGuard;
        }

        private Task<ManagerDelegation?> GuardAsync(Guid actingAsUserId, ActingOnBehalf? onBehalf) =>
            _delegationGuard.GuardAsync(actingAsUserId, onBehalf);

        private static string ActorLabel(User actingAs, ManagerDelegation? delegation) =>
            DelegationGuard.ActorLabel(actingAs, delegation);

        private Task RecordDelegatedActionAsync(
            ManagerDelegation? delegation, Guid actingAsUserId, Guid targetUserId,
            DelegatedActionType actionType, Guid targetEntityId, string? details) =>
            _delegationGuard.RecordDelegatedActionAsync(delegation, actingAsUserId, targetUserId, actionType, targetEntityId, details);

        public async Task<List<LeaveRequest>> GetPendingRequestsForManagerAsync(Guid managerId)
        {
            var manager = await GetUserOrThrowAsync(managerId);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var directPending = (await _leaveRequestGateway.GetPendingRequestsByManagerAsync(managerId))
                .Where(request => request.User.RegionId == manager.RegionId)
                .ToList();

            // Also check for delegated managers
            var delegatedManagerIds = await _delegationGateway.GetDelegatedManagerIdsAsync(managerId, today);
            if (delegatedManagerIds.Count == 0)
                return directPending;

            var allRequests = new List<LeaveRequest>(directPending);
            foreach (var delegatedManagerId in delegatedManagerIds)
            {
                var delegatedPending = await _leaveRequestGateway.GetPendingRequestsByManagerAsync(delegatedManagerId);
                allRequests.AddRange(delegatedPending.Where(request => request.User.RegionId == manager.RegionId));
            }

            return allRequests.DistinctBy(r => r.Id).OrderBy(r => r.StartDate).ToList();
        }

        // Scoped to the manager's own direct reports and delegated requests.
        public async Task<ManagerDashboardResult> GetManagerDashboardAsync(Guid managerId)
        {
            var manager = await GetUserOrThrowAsync(managerId);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var result = new ManagerDashboardResult();

            // Load active delegations
            result.ActiveDelegationsGiven = (await _delegationGateway.GetActiveDelegationsForManagerAsync(managerId, today))
                .Where(delegation => delegation.Delegate.RegionId == manager.RegionId)
                .ToList();
            result.ActiveDelegationsReceived = (await _delegationGateway.GetActiveDelegationsForDelegateAsync(managerId, today))
                .Where(delegation => delegation.Manager.RegionId == manager.RegionId)
                .ToList();

            var reports = (await _userGateway.GetDirectReportsAsync(managerId))
                .Where(report => report.RegionId == manager.RegionId)
                .ToList();
            result.TeamSize = reports.Count;

            var reportIds = reports.Select(p => p.Id).ToList();

            var onLeave = reportIds.Count > 0
                ? await _leaveRequestGateway.GetApprovedRequestsForUsersAsync(reportIds, today, today)
                : new List<LeaveRequest>();

            var outToday = new HashSet<Guid>(onLeave.Select(r => r.UserId));
            result.OnLeaveToday = outToday.Count;

            foreach (var person in reports)
            {
                var activeContract = await _contractGateway.GetActiveContractByUserIdAsync(person.Id);

                result.Team.Add(new ManagerTeamMember
                {
                    Id = person.Id,
                    Name = person.Name,
                    Role = person.Role.ToString(),
                    Department = person.Department == null ? "—" : person.Department.Name,
                    OnLeaveToday = outToday.Contains(person.Id),
                    ContractId = activeContract?.Id,
                    ContractType = activeContract?.Type,
                    ContractStatus = activeContract?.Status,
                    ContractStartDate = activeContract?.StartDate,
                    ContractEndDate = activeContract?.EndDate
                });
            }

            // Direct pending requests
            var pending = (await _leaveRequestGateway.GetPendingRequestsByManagerAsync(managerId))
                .Where(request => request.User.RegionId == manager.RegionId)
                .ToList();
            foreach (var request in pending)
            {
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
                    Days = await CountWorkingDaysAsync(manager, request.StartDate, request.EndDate),
                    WaitingDays = waiting,
                    IsDelegated = false,
                    Role = request.User.Role.ToString(),
                    Reason = request.Reason,
                    SubmittedAt = request.CreatedAt
                });
            }

            // Delegated pending requests
            foreach (var delegation in result.ActiveDelegationsReceived)
            {
                var delegatedPending = await _leaveRequestGateway.GetPendingRequestsByManagerAsync(delegation.ManagerId);
                foreach (var request in delegatedPending.Where(request => request.User.RegionId == manager.RegionId))
                {
                    if (result.Pending.Any(p => p.RequestId == request.Id))
                        continue;

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
                        Days = await CountWorkingDaysAsync(manager, request.StartDate, request.EndDate),
                        WaitingDays = waiting,
                        IsDelegated = true,
                        DelegatedFromManagerName = delegation.Manager?.Name ?? "Delegated Manager",
                        Role = request.User.Role.ToString(),
                        Reason = request.Reason,
                        SubmittedAt = request.CreatedAt
                    });
                }
            }

            result.PendingRequests = result.Pending.Count;
            return result;
        }

        public async Task<LeaveRequest> DecideRequestAsync(
            Guid managerId, Guid requestId, bool approve, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(managerId, onBehalf);
            var manager = await GetUserOrThrowAsync(managerId);
            var request = await _leaveRequestGateway.GetRequestByIdAsync(requestId);
            if (request == null)
                throw new EntityNotFoundException($"No leave request with id {requestId}.");
            if (request.User.RegionId != manager.RegionId)
                throw new UnauthorizedException("You cannot review requests from another region.");

            if (request.Status != LeaveStatus.Pending)
                throw new InvalidOperationException("This request has already been decided.");

            var today = DateOnly.FromDateTime(DateTime.Today);
            var isDirectManager = request.User.ManagerId == managerId;
            var isAuthorizedDelegate = false;

            if (!isDirectManager && request.User.ManagerId.HasValue)
            {
                var delegatedManagerIds = await _delegationGateway.GetDelegatedManagerIdsAsync(managerId, today);
                isAuthorizedDelegate = delegatedManagerIds.Contains(request.User.ManagerId.Value);
            }

            if (!isDirectManager && !isAuthorizedDelegate)
                throw new UnauthorizedException("You are not this employee's manager or active delegate.");

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

            var isFinal = !approve || LeaveApprovalPolicy.IsFullyApproved(request, requirement);
            if (isFinal)
                request.Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;

            await _leaveRequestGateway.SaveDecisionAsync(request, approval);

            var period = request.StartDate.ToString("MMM d", CultureInfo.InvariantCulture)
                         + " – " +
                         request.EndDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

            await RecordDelegatedActionAsync(
                delegation, managerId, request.UserId,
                approve ? DelegatedActionType.LeaveApproved : DelegatedActionType.LeaveRejected,
                request.Id, $"{request.Type} leave, {period}");

            try
            {
                var actor = ActorLabel(manager, delegation);

                var notificationMessage = isFinal
                    ? $"Your {request.Type} leave request for {period} was {(request.Status == LeaveStatus.Approved ? "approved" : "declined")} by {actor}."
                    : $"Your {request.Type} leave request for {period} was approved by {actor} and is now awaiting HR approval.";

                await _notifications.SendNotificationAsync(
                    request.UserId,
                    notificationMessage,
                    "/employee/my-requests");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Decision on {RequestId} saved but the notification failed.", requestId);
            }

            _logger.LogInformation("Manager/Delegate {ManagerId} {Decision} leave request {RequestId}{Final}.",
                managerId, approve ? "approved" : "rejected", requestId, isFinal ? "" : " (still awaiting HR)");
            return request;
        }



        private async Task<User> GetUserOrThrowAsync(Guid userId)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            return user ?? throw new EntityNotFoundException($"No user with id {userId}.");
        }
    }
}
