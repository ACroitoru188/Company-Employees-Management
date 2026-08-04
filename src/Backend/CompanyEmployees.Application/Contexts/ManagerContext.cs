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

        public ManagerContext(
            ILogger<ManagerContext> logger,
            ILeaveRequestGateway leaveRequestGateway,
            IUserGateway userGateway,
            IContractGateway contractGateway,
            IManagerDelegationGateway delegationGateway,
            NotificationContext notifications) : base(logger)
        {
            _leaveRequestGateway = leaveRequestGateway;
            _userGateway = userGateway;
            _contractGateway = contractGateway;
            _delegationGateway = delegationGateway;
            _notifications = notifications;
        }

        public async Task<List<LeaveRequest>> GetPendingRequestsForManagerAsync(Guid managerId)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var directPending = await _leaveRequestGateway.GetPendingRequestsByManagerAsync(managerId);

            // Also check for delegated managers
            var delegatedManagerIds = await _delegationGateway.GetDelegatedManagerIdsAsync(managerId, today);
            if (delegatedManagerIds.Count == 0)
                return directPending;

            var allRequests = new List<LeaveRequest>(directPending);
            foreach (var delegatedManagerId in delegatedManagerIds)
            {
                var delegatedPending = await _leaveRequestGateway.GetPendingRequestsByManagerAsync(delegatedManagerId);
                allRequests.AddRange(delegatedPending);
            }

            return allRequests.DistinctBy(r => r.Id).OrderBy(r => r.StartDate).ToList();
        }

        // Scoped to the manager's own direct reports and delegated requests.
        public async Task<ManagerDashboardResult> GetManagerDashboardAsync(Guid managerId)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var result = new ManagerDashboardResult();

            // Load active delegations
            result.ActiveDelegationsGiven = await _delegationGateway.GetActiveDelegationsForManagerAsync(managerId, today);
            result.ActiveDelegationsReceived = await _delegationGateway.GetActiveDelegationsForDelegateAsync(managerId, today);

            var reports = await _userGateway.GetDirectReportsAsync(managerId);
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
            var pending = await _leaveRequestGateway.GetPendingRequestsByManagerAsync(managerId);
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
                    Days = request.EndDate.DayNumber - request.StartDate.DayNumber + 1,
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
                foreach (var request in delegatedPending)
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
                        Days = request.EndDate.DayNumber - request.StartDate.DayNumber + 1,
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

        public async Task<LeaveRequest> DecideRequestAsync(Guid managerId, Guid requestId, bool approve)
        {
            var request = await _leaveRequestGateway.GetRequestByIdAsync(requestId);
            if (request == null)
                throw new EntityNotFoundException($"No leave request with id {requestId}.");

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

            try
            {
                var period = request.StartDate.ToString("MMM d", CultureInfo.InvariantCulture)
                             + " – " +
                             request.EndDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
                
                string notificationMessage;
                if (isFinal)
                {
                    notificationMessage = $"Your {request.Type} leave request for {period} was {(request.Status == LeaveStatus.Approved ? "approved" : "declined")}.";
                }
                else
                {
                    notificationMessage = $"Your {request.Type} leave request for {period} was approved by {(isAuthorizedDelegate ? "acting delegate manager" : "your manager")} and is now awaiting HR approval.";
                }

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

        public async Task ExtendContractAsync(Guid managerId, Guid contractId, DateOnly newEndDate)
        {
            var contract = await _contractGateway.GetByIdAsync(contractId);
            if (contract == null)
                throw new EntityNotFoundException($"Contract with ID {contractId} not found.");

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

            try
            {
                await _notifications.SendNotificationAsync(
                    contract.UserId,
                    $"Your employment contract has been extended to {newEndDate:yyyy-MM-dd} by your line manager.",
                    "/employee/profile");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Contract extension for {ContractId} succeeded but notification failed.", contractId);
            }

            _logger.LogInformation("Manager {ManagerId} extended contract {ContractId} (User {UserId}) from {PreviousEnd} to {NewEnd}.",
                managerId, contractId, contract.UserId, previousEnd, newEndDate);
        }

        public async Task TerminateContractAsync(Guid managerId, Guid contractId, string? reason)
        {
            var contract = await _contractGateway.GetByIdAsync(contractId);
            if (contract == null)
                throw new EntityNotFoundException($"Contract with ID {contractId} not found.");

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

            try
            {
                await _notifications.SendNotificationAsync(
                    contract.UserId,
                    $"Your employment contract has been terminated by your line manager. Reason: {reason ?? "No reason specified"}.",
                    "/employee/profile");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Contract termination for {ContractId} succeeded but notification failed.", contractId);
            }

            _logger.LogInformation("Manager {ManagerId} terminated contract {ContractId} (User {UserId}). Reason: {Reason}",
                managerId, contractId, contract.UserId, reason);
        }

        public async Task<ManagerDelegation> CreateDelegationAsync(Guid managerId, Guid delegateId, DateOnly start, DateOnly end, string? reason)
        {
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

                await _notifications.SendNotificationAsync(
                    delegateId,
                    $"You have been assigned as temporary Line Manager delegate for {period}.",
                    "/manager/team");
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

        public Task<List<ManagerDelegation>> GetMyDelegationsAsync(Guid managerId)
        {
            return _delegationGateway.GetAllDelegationsByManagerAsync(managerId);
        }

        public Task<List<ManagerDelegation>> GetActiveDelegationsAssignedToMeAsync(Guid delegateId)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return _delegationGateway.GetActiveDelegationsForDelegateAsync(delegateId, today);
        }
    }
}
