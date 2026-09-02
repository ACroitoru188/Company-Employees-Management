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
        private readonly IPublicHolidayProvider _holidayProvider;
        private readonly NotificationContext _notifications;
        private readonly ImpersonationContext _impersonation;
        private readonly IDelegatedActionGateway _delegatedActions;

        public ManagerContext(
            ILogger<ManagerContext> logger,
            ILeaveRequestGateway leaveRequestGateway,
            IUserGateway userGateway,
            IContractGateway contractGateway,
            IManagerDelegationGateway delegationGateway,
            IPublicHolidayProvider holidayProvider,
            NotificationContext notifications,
            ImpersonationContext impersonation,
            IDelegatedActionGateway delegatedActions) : base(logger)
        {
            _leaveRequestGateway = leaveRequestGateway;
            _userGateway = userGateway;
            _contractGateway = contractGateway;
            _delegationGateway = delegationGateway;
            _holidayProvider = holidayProvider;
            _notifications = notifications;
            _impersonation = impersonation;
            _delegatedActions = delegatedActions;
        }

        // Every action taken from inside a borrowed account passes through here first. It
        // delegates to ImpersonationContext rather than re-checking the window locally, so
        // the rule for "is this delegation still good" has exactly one implementation.
        //
        // Returns the delegation, which arrives with Manager and Delegate loaded — that is
        // where the audit row and the notification get the real actor's name, without a
        // second lookup. Null means the caller is acting as themselves.
        private async Task<ManagerDelegation?> GuardAsync(Guid actingAsUserId, ActingOnBehalf? onBehalf)
        {
            if (onBehalf is null)
                return null;

            return await _impersonation.ValidateDelegationAsync(
                onBehalf.RealUserId, onBehalf.DelegationId, actingAsUserId);
        }

        // "Line Manager Mihai Georgescu" or, when someone is covering for him,
        // "Line Manager Mihai Georgescu (delegate: Elena Vasilescu)". The account that
        // carries the authority is named first — the delegate is the parenthetical.
        private static string ActorLabel(User actingAs, ManagerDelegation? delegation)
        {
            var who = actingAs.Role == UserRole.LineManager
                ? $"Line Manager {actingAs.Name}"
                : actingAs.Name;

            return delegation is null ? who : $"{who} (delegate: {delegation.Delegate.Name})";
        }

        private Task RecordDelegatedActionAsync(
            ManagerDelegation? delegation, Guid actingAsUserId, Guid targetUserId,
            DelegatedActionType actionType, Guid targetEntityId, string? details)
        {
            if (delegation is null)
                return Task.CompletedTask;

            return _delegatedActions.CreateAsync(new DelegatedAction
            {
                Id = Guid.NewGuid(),
                DelegationId = delegation.Id,
                RealUserId = delegation.DelegateId,
                ActedAsUserId = actingAsUserId,
                TargetUserId = targetUserId,
                ActionType = actionType,
                TargetEntityId = targetEntityId,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });
        }

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

        private async Task<int> CountWorkingDaysAsync(User user, DateOnly start, DateOnly end)
        {
            var holidays = new HashSet<DateOnly>();
            for (var year = start.Year; year <= end.Year; year++)
            {
                foreach (var holiday in await _holidayProvider.GetHolidaysAsync(user.Region.Code, year))
                    holidays.Add(holiday.Date);
            }

            var count = 0;
            for (var day = start; day <= end; day = day.AddDays(1))
            {
                if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                    && !holidays.Contains(day))
                    count++;
            }

            return count;
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

        public async Task ExtendContractAsync(
            Guid managerId, Guid contractId, DateOnly newEndDate, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(managerId, onBehalf);
            var manager = await GetUserOrThrowAsync(managerId);
            var contract = await _contractGateway.GetByIdAsync(contractId);
            if (contract == null)
                throw new EntityNotFoundException($"Contract with ID {contractId} not found.");
            if (contract.User.RegionId != manager.RegionId)
                throw new UnauthorizedException("You cannot manage contracts from another region.");

            // Check authorization: direct manager or active delegate
            var today = DateOnly.FromDateTime(DateTime.Today);
            var isDirectManager = contract.User.ManagerId == managerId;
            var isAuthorizedDelegate = false;

            if (!isDirectManager && contract.User.ManagerId.HasValue)
            {
                var delegatedManagerIds = await _delegationGateway.GetDelegatedManagerIdsAsync(managerId, today);
                isAuthorizedDelegate = delegatedManagerIds.Contains(contract.User.ManagerId.Value);
            }

            if (!isDirectManager && !isAuthorizedDelegate)
                throw new UnauthorizedException("You are not authorized to manage this contract.");

            if (contract.Type != ContractType.Determinate)
                throw new InvalidOperationException("Only determinate (fixed-term) contracts can have their end date extended.");

            if (contract.Status != ContractStatus.Active)
                throw new InvalidOperationException("Only active contracts can be extended.");

            if (newEndDate <= contract.StartDate)
                throw new InvalidOperationException("New end date must be after the contract start date.");

            if (contract.EndDate.HasValue && newEndDate <= contract.EndDate.Value)
                throw new InvalidOperationException("New end date must be strictly after the current end date.");

            var previousEnd = contract.EndDate?.ToString("yyyy-MM-dd") ?? "none";
            contract.EndDate = newEndDate;
            contract.UpdatedAt = DateTime.UtcNow;

            await _contractGateway.UpdateAsync(contract);

            await RecordDelegatedActionAsync(
                delegation, managerId, contract.UserId, DelegatedActionType.ContractExtended,
                contract.Id, $"End date {previousEnd} → {newEndDate:yyyy-MM-dd}");

            try
            {
                await _notifications.SendNotificationAsync(
                    contract.UserId,
                    $"Your employment contract has been extended to {newEndDate:yyyy-MM-dd} by {ActorLabel(manager, delegation)}.",
                    "/employee/profile");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Contract extension for {ContractId} succeeded but notification failed.", contractId);
            }

            _logger.LogInformation("Manager {ManagerId} extended contract {ContractId} (User {UserId}) from {PreviousEnd} to {NewEnd}.",
                managerId, contractId, contract.UserId, previousEnd, newEndDate);
        }

        public async Task TerminateContractAsync(
            Guid managerId, Guid contractId, string? reason, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(managerId, onBehalf);
            var manager = await GetUserOrThrowAsync(managerId);
            var contract = await _contractGateway.GetByIdAsync(contractId);
            if (contract == null)
                throw new EntityNotFoundException($"Contract with ID {contractId} not found.");
            if (contract.User.RegionId != manager.RegionId)
                throw new UnauthorizedException("You cannot manage contracts from another region.");

            var today = DateOnly.FromDateTime(DateTime.Today);
            var isDirectManager = contract.User.ManagerId == managerId;
            var isAuthorizedDelegate = false;

            if (!isDirectManager && contract.User.ManagerId.HasValue)
            {
                var delegatedManagerIds = await _delegationGateway.GetDelegatedManagerIdsAsync(managerId, today);
                isAuthorizedDelegate = delegatedManagerIds.Contains(contract.User.ManagerId.Value);
            }

            if (!isDirectManager && !isAuthorizedDelegate)
                throw new UnauthorizedException("You are not authorized to manage this contract.");

            if (contract.Status == ContractStatus.Terminated)
                throw new InvalidOperationException("Contract is already terminated.");

            contract.Status = ContractStatus.Terminated;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                contract.Notes = string.IsNullOrWhiteSpace(contract.Notes)
                    ? $"[Terminated: {reason}]"
                    : $"{contract.Notes} | [Terminated: {reason}]";
            }
            contract.UpdatedAt = DateTime.UtcNow;

            await _contractGateway.UpdateAsync(contract);

            await RecordDelegatedActionAsync(
                delegation, managerId, contract.UserId, DelegatedActionType.ContractTerminated,
                contract.Id, reason);

            try
            {
                await _notifications.SendNotificationAsync(
                    contract.UserId,
                    $"Your employment contract has been terminated by {ActorLabel(manager, delegation)}. Reason: {reason ?? "No reason specified"}.",
                    "/employee/profile");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Contract termination for {ContractId} succeeded but notification failed.", contractId);
            }

            _logger.LogInformation("Manager {ManagerId} terminated contract {ContractId} (User {UserId}). Reason: {Reason}",
                managerId, contractId, contract.UserId, reason);
        }

        public async Task<ManagerDelegation> CreateDelegationAsync(
            Guid managerId, Guid delegateId, DateOnly start, DateOnly end, string? reason,
            ActingOnBehalf? onBehalf = null)
        {
            // No chaining: authority that was lent cannot be lent onward. Only the account's
            // real owner may hand it to someone else.
            if (onBehalf is not null)
                throw new UnauthorizedException("You cannot delegate from an account you are only borrowing.");

            if (managerId == delegateId)
                throw new InvalidOperationException("You cannot delegate responsibilities to yourself.");

            if (start > end)
                throw new InvalidOperationException("End date cannot be earlier than start date.");

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (end < today)
                throw new InvalidOperationException("Cannot create a delegation for a period that has already ended.");

            var delegateUser = await _userGateway.GetUserByIdAsync(delegateId);
            if (delegateUser == null || delegateUser.Status != UserStatus.Active)
                throw new EntityNotFoundException("Selected delegate user was not found or is inactive.");

            var manager = await GetUserOrThrowAsync(managerId);
            if (delegateUser.RegionId != manager.RegionId)
                throw new UnauthorizedException("You can only delegate to a manager in your region.");

            // One stand-in at a time: a second delegation overlapping an existing one — to the
            // same person or someone else — leaves two people acting for the same account on the
            // same day, which the audit trail and the "who is covering" UI both assume can't
            // happen.
            if (await _delegationGateway.HasActiveDelegationInPeriodAsync(managerId, start, end))
                throw new InvalidOperationException(
                    "You already have a delegation covering part of this period. Cancel it first or choose different dates.");

            var delegation = new ManagerDelegation
            {
                Id = Guid.NewGuid(),
                ManagerId = managerId,
                DelegateId = delegateId,
                StartDate = start,
                EndDate = end,
                Reason = reason,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _delegationGateway.CreateAsync(delegation);

            try
            {
                var period = start.ToString("MMM d", CultureInfo.InvariantCulture)
                             + " – " +
                             end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

                // An employee delegate has no approval duties and no access to /manager/team,
                // so neither the old wording nor the old link held once employees could
                // delegate. The delegations page is where every delegate starts, whatever the
                // borrowed account can do.
                var message = manager.Role == UserRole.LineManager
                    ? $"You have been assigned as temporary Line Manager delegate for {manager.Name}, {period}."
                    : $"{manager.Name} asked you to cover for them, {period}.";

                await _notifications.SendNotificationAsync(delegateId, message, "/employee/delegations");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Delegation creation succeeded but notification failed.");
            }

            _logger.LogInformation("Manager {ManagerId} created delegation to {DelegateId} from {Start} to {End}.",
                managerId, delegateId, start, end);

            return delegation;
        }

        public async Task CancelDelegationAsync(Guid managerId, Guid delegationId)
        {
            var delegation = await _delegationGateway.GetByIdAsync(delegationId);
            if (delegation == null)
                throw new EntityNotFoundException($"Delegation with ID {delegationId} not found.");

            if (delegation.ManagerId != managerId)
                throw new UnauthorizedException("You are not authorized to cancel this delegation.");

            delegation.IsActive = false;
            await _delegationGateway.UpdateAsync(delegation);

            _logger.LogInformation("Manager {ManagerId} cancelled delegation {DelegationId}.", managerId, delegationId);
        }

        // Reads the audit rows this context writes. The two personal scopes are filtered to
        // the caller and need no further authorisation; the region-wide one is oversight and
        // is checked here, so hiding the tab is not what protects it.
        public async Task<DelegationHistoryResult> GetDelegationHistoryAsync(
            Guid userId, DelegationHistoryScope scope, int skip, int take)
        {
            List<DelegatedAction> actions;
            int total;
            string? regionName = null;

            if (scope == DelegationHistoryScope.EveryoneInRegion)
            {
                var caller = await GetUserOrThrowAsync(userId);
                if (caller.Role != UserRole.Admin)
                    throw new UnauthorizedException("Only an administrator can view the whole region's history.");

                actions = await _delegatedActions.GetForRegionAsync(caller.RegionId, skip, take);
                total = await _delegatedActions.CountForRegionAsync(caller.RegionId);
                regionName = caller.Region?.Name;
            }
            else if (scope == DelegationHistoryScope.DoneInMyName)
            {
                actions = await _delegatedActions.GetActedAsAsync(userId, skip, take);
                total = await _delegatedActions.CountActedAsAsync(userId);
            }
            else
            {
                actions = await _delegatedActions.GetPerformedByAsync(userId, skip, take);
                total = await _delegatedActions.CountPerformedByAsync(userId);
            }

            return new DelegationHistoryResult
            {
                Total = total,
                RegionName = regionName,
                Items = actions.Select(action => new DelegationHistoryEntry
                {
                    When = action.CreatedAt,
                    RealUserName = action.RealUser.Name,
                    ActedAsName = action.ActedAsUser.Name,
                    TargetName = action.TargetUser.Name,
                    ActionType = action.ActionType,
                    Details = action.Details
                }).ToList()
            };
        }

        public async Task<List<ManagerDelegation>> GetMyDelegationsAsync(Guid managerId)
        {
            var manager = await GetUserOrThrowAsync(managerId);
            return (await _delegationGateway.GetAllDelegationsByManagerAsync(managerId))
                .Where(delegation => delegation.Delegate.RegionId == manager.RegionId)
                .ToList();
        }

        public async Task<List<ManagerDelegation>> GetActiveDelegationsAssignedToMeAsync(Guid delegateId)
        {
            var delegateUser = await GetUserOrThrowAsync(delegateId);
            var today = DateOnly.FromDateTime(DateTime.Today);
            return (await _delegationGateway.GetActiveDelegationsForDelegateAsync(delegateId, today))
                .Where(delegation => delegation.Manager.RegionId == delegateUser.RegionId)
                .ToList();
        }

        private async Task<User> GetUserOrThrowAsync(Guid userId)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            return user ?? throw new EntityNotFoundException($"No user with id {userId}.");
        }
    }
}
