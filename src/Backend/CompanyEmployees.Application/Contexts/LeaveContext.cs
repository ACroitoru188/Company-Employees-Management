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
    public class LeaveContext : BaseContext
    {
        private readonly ILeaveRequestGateway _leaveRequestGateway;
        private readonly IUserGateway _userGateway;
        private readonly IContractGateway _contractGateway;
        private readonly IManagerDelegationGateway _delegationGateway;
        private readonly NotificationContext _notifications;
        private readonly DelegationGuard _delegationGuard;

        public LeaveContext(
            ILogger<LeaveContext> logger,
            ILeaveRequestGateway leaveRequestGateway,
            IUserGateway userGateway,
            IContractGateway contractGateway,
            IManagerDelegationGateway delegationGateway,
            IPublicHolidayProvider holidayProvider,
            NotificationContext notifications,
            DelegationGuard delegationGuard) : base(logger, holidayProvider)
        {
            _leaveRequestGateway = leaveRequestGateway;
            _userGateway = userGateway;
            _contractGateway = contractGateway;
            _delegationGateway = delegationGateway;
            _notifications = notifications;
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

        public Task<List<LeaveRequest>> GetMyRequestsAsync(Guid userId)
        {
            return _leaveRequestGateway.GetRequestsByUserAsync(userId);
        }

        public async Task<List<LeaveBalanceResult>> GetMyBalancesAsync(
            Guid userId,
            int year,
            DateOnly? asOf = null)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            await _leaveRequestGateway.EnsureDefaultAllocationsAsync(userId, year);
            var allocations = await _leaveRequestGateway.GetAllocationsByUserAsync(userId, year);
            var requests = await _leaveRequestGateway.GetRequestsByUserAsync(userId);
            var companyStartDate = (await _contractGateway.GetContractsByUserIdAsync(userId))
                .OrderBy(contract => contract.StartDate)
                .Select(contract => (DateOnly?)contract.StartDate)
                .FirstOrDefault();
            var holidays = (await _holidayProvider!.GetHolidaysAsync(user.Region.Code, year))
                .Select(holiday => holiday.Date)
                .ToHashSet();
            var annualEntitlement = LeaveAllocationPolicy.AnnualDaysForRegion(
                user.Region.Code,
                companyStartDate,
                year);
            var annualCarryOver = await GetAnnualCarryOverAsync(
                user,
                year,
                requests,
                companyStartDate,
                holidays,
                asOf ?? DateOnly.FromDateTime(DateTime.Today));
            var annualCarryOverDays = annualCarryOver.Sum(portion => portion.Days);

            var balances = new List<LeaveBalanceResult>();
            foreach (var allocation in allocations)
            {
                var daysUsed = requests
                    .Where(r => r.Status == LeaveStatus.Approved
                                && r.Type == allocation.LeaveType
                                && r.StartDate.Year == year)
                    .Sum(r => CountWorkingDays(r.StartDate, r.EndDate, holidays));

                balances.Add(new LeaveBalanceResult
                {
                    Type = allocation.LeaveType,
                    DaysTotal = (allocation.LeaveType == LeaveType.Annual
                        ? annualEntitlement
                        : allocation.NumberOfDays)
                        + (allocation.LeaveType == LeaveType.Annual ? annualCarryOverDays : 0),
                    DaysUsed = daysUsed,
                    CarryOverPortions = allocation.LeaveType == LeaveType.Annual
                        ? annualCarryOver
                        : []
                });
            }
            return balances;
        }

        private async Task<List<AnnualCarryOverPortionResult>> GetAnnualCarryOverAsync(
            User user,
            int year,
            IReadOnlyCollection<LeaveRequest> requests,
            DateOnly? companyStartDate,
            HashSet<DateOnly> currentYearHolidays,
            DateOnly asOf)
        {
            var previousYear = year - 1;
            var previousAnnualAllocation = (await _leaveRequestGateway
                    .GetAllocationsByUserAsync(user.Id, previousYear))
                .FirstOrDefault(allocation => allocation.LeaveType == LeaveType.Annual);

            if (previousAnnualAllocation == null)
                return [];

            var previousYearHolidays = (await _holidayProvider!
                    .GetHolidaysAsync(user.Region.Code, previousYear))
                .Select(holiday => holiday.Date)
                .ToHashSet();
            var previousYearUsed = AnnualDaysUsed(requests, previousYear, previousYearHolidays);

            // A portion remains available into its second carry-over calendar year.
            // Previous-year leave consumes that older portion first, preserving the newer
            // entitlement and its later expiry date.
            var twoYearsAgo = year - 2;
            var twoYearsAgoAllocation = (await _leaveRequestGateway
                    .GetAllocationsByUserAsync(user.Id, twoYearsAgo))
                .FirstOrDefault(allocation => allocation.LeaveType == LeaveType.Annual);
            var olderCarryAtPreviousYearStart = 0;
            if (twoYearsAgoAllocation != null)
            {
                var twoYearsAgoHolidays = (await _holidayProvider!
                        .GetHolidaysAsync(user.Region.Code, twoYearsAgo))
                    .Select(holiday => holiday.Date)
                    .ToHashSet();
                var twoYearsAgoUsed = AnnualDaysUsed(requests, twoYearsAgo, twoYearsAgoHolidays);
                olderCarryAtPreviousYearStart = LeaveAllocationPolicy.AnnualCarryOverDays(
                    LeaveAllocationPolicy.AnnualDaysForRegion(
                        user.Region.Code,
                        companyStartDate,
                        twoYearsAgo),
                    twoYearsAgoUsed);
            }

            var olderCarryAtYearStart = Math.Max(
                0,
                olderCarryAtPreviousYearStart - previousYearUsed);
            var previousEntitlementUsed = Math.Max(
                0,
                previousYearUsed - olderCarryAtPreviousYearStart);
            var recentCarryAtYearStart = LeaveAllocationPolicy.AnnualCarryOverDays(
                LeaveAllocationPolicy.AnnualDaysForRegion(
                    user.Region.Code,
                    companyStartDate,
                    previousYear),
                previousEntitlementUsed);

            var portions = new List<AnnualCarryOverPortionResult>();
            if (olderCarryAtYearStart > 0)
            {
                var olderExpiry = LeaveAllocationPolicy.AnnualCarryOverExpiryDate(previousYear);
                var usedBeforeOlderExpiry = requests
                    .Where(request => request.Status == LeaveStatus.Approved
                                      && request.Type == LeaveType.Annual
                                      && request.StartDate.Year == year
                                      && request.StartDate <= olderExpiry)
                    .Sum(request => CountWorkingDays(
                        request.StartDate,
                        request.EndDate < olderExpiry ? request.EndDate : olderExpiry,
                        currentYearHolidays));
                var expired = LeaveAllocationPolicy.ExpiredAnnualCarryOverDays(
                    olderCarryAtYearStart,
                    Math.Min(olderCarryAtYearStart, usedBeforeOlderExpiry),
                    previousYear,
                    asOf);
                portions.Add(new(olderCarryAtYearStart, olderExpiry, expired));
            }

            if (recentCarryAtYearStart > 0)
            {
                portions.Add(new(
                    recentCarryAtYearStart,
                    LeaveAllocationPolicy.AnnualCarryOverExpiryDate(year),
                    0));
            }

            return portions;
        }

        private static int AnnualDaysUsed(
            IEnumerable<LeaveRequest> requests,
            int year,
            HashSet<DateOnly> holidays) =>
            requests
                .Where(request => request.Status == LeaveStatus.Approved
                                  && request.Type == LeaveType.Annual
                                  && request.StartDate.Year == year)
                .Sum(request => CountWorkingDays(request.StartDate, request.EndDate, holidays));

        public async Task<IReadOnlyList<PublicHoliday>> GetRegionalHolidaysAsync(Guid userId, int year)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            return await _holidayProvider!.GetHolidaysAsync(user.Region.Code, year);
        }

        // Everyone sees pending and approved requests for their own team so employees
        // have the same staffing visibility as their line manager. Team membership and
        // region boundaries are still enforced below.
        public async Task<List<LeaveRequest>> GetTeamRequestsAsync(Guid userId, DateOnly from, DateOnly to)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            if (user.Role == UserRole.LineManager)
            {
                var directReports = await _userGateway.GetDirectReportsAsync(userId);
                var directReportIds = directReports
                    .Where(report => report.RegionId == user.RegionId)
                    .Select(report => report.Id)
                    .ToList();

                return directReportIds.Count == 0
                    ? []
                    : await _leaveRequestGateway.GetActiveRequestsForUsersAsync(
                        directReportIds, from, to);
            }

            var team = await GetTeamMembersAsync(userId);

            // Include the signed-in employee as well as their manager and peers. Without
            // this, an employee's own leave appeared on the line-manager calendar but
            // disappeared when that employee opened the same team calendar.
            var teamIds = new List<Guid> { userId };
            foreach (var member in team)
            {
                teamIds.Add(member.Id);
            }

            if (teamIds.Count == 0)
                return [];

            return await _leaveRequestGateway.GetActiveRequestsForUsersAsync(teamIds, from, to);
        }

        // The user's team: their manager first, then the active colleagues who
        // share the same manager. A user without a manager has no team.
        public async Task<List<User>> GetTeamMembersAsync(Guid userId)
        {
            var me = await _userGateway.GetUserByIdAsync(userId);
            if (me == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            var team = new List<User>();
            if (me.ManagerId == null)
                return team;

            var manager = await _userGateway.GetUserByIdAsync(me.ManagerId.Value);
            if (manager != null && manager.RegionId == me.RegionId)
                team.Add(manager);

            var allUsers = await _userGateway.GetAllUsersAsync();
            foreach (var user in allUsers)
            {
                if (user.ManagerId == me.ManagerId
                    && user.Id != userId
                    && user.Status == UserStatus.Active
                    && user.RegionId == me.RegionId)
                    team.Add(user);
            }
            return team;
        }

        // Org-wide figures for the HR dashboard. One call, so the page issues a
        // single round trip instead of several against the same scoped context.
        public async Task<HrDashboardResult> GetHrDashboardAsync(Guid hrUserId)
        {
            var hrUser = await _userGateway.GetUserByIdAsync(hrUserId);
            if (hrUser == null)
                throw new EntityNotFoundException($"No user with id {hrUserId}.");

            var today = DateOnly.FromDateTime(DateTime.Today);
            var result = new HrDashboardResult();

            var users = (await _userGateway.GetAllUsersAsync())
                .Where(user => user.RegionId == hrUser.RegionId)
                .ToList();
            var activeIds = new List<Guid>();
            var perDepartment = new Dictionary<string, int>();

            foreach (var user in users)
            {
                if (user.Status != UserStatus.Active)
                    continue;

                result.ActiveEmployees++;
                activeIds.Add(user.Id);

                if (user.CreatedAt >= DateTime.UtcNow.AddDays(-30))
                    result.NewEmployees++;

                var department = user.Department == null ? "No department" : user.Department.Name;
                if (perDepartment.ContainsKey(department))
                    perDepartment[department]++;
                else
                    perDepartment[department] = 1;
            }

            foreach (var entry in perDepartment)
            {
                result.Departments.Add(new HrDepartmentCount
                {
                    Name = entry.Key,
                    Count = entry.Value
                });
            }
            result.Departments.Sort((a, b) => b.Count.CompareTo(a.Count));

            var pending = (await _leaveRequestGateway.GetAllPendingRequestsAsync())
                .Where(request => request.User.RegionId == hrUser.RegionId)
                .ToList();
            result.PendingRequests = pending.Count;

            foreach (var request in pending)
            {
                var waiting = (DateTime.UtcNow - request.CreatedAt).Days;
                if (waiting > 7)
                    result.StaleRequests++;

                result.Pending.Add(new HrPendingRequest
                {
                    RequestId = request.Id,
                    Name = request.User.Name,
                    Department = request.User.Department == null ? "—" : request.User.Department.Name,
                    Type = request.Type.ToString(),
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    Days = await CountWorkingDaysAsync(hrUser, request.StartDate, request.EndDate),
                    WaitingDays = waiting,
                    Role = request.User.Role.ToString(),
                    Reason = request.Reason,
                    SubmittedAt = request.CreatedAt
                });
            }

            // Approved leave that covers today tells HR who is out right now.
            if (activeIds.Count > 0)
            {
                var todaysLeave = await _leaveRequestGateway
                    .GetApprovedRequestsForUsersAsync(activeIds, today, today);
                result.OnLeaveToday = todaysLeave.Count;

                // Approved leave whose owner has asked for it back. The gateway matches on
                // overlap, so an open-ended "to" gives exactly EndDate >= today: upcoming plus
                // in progress, and nothing already served out.
                var approved = await _leaveRequestGateway
                    .GetApprovedRequestsForUsersAsync(activeIds, today, DateOnly.MaxValue);

                var wanted = approved
                    .Where(request => request.CancellationRequestedAt is not null)
                    .OrderBy(request => request.CancellationRequestedAt)
                    .ToList();

                foreach (var request in wanted)
                {
                    result.CancellationRequests.Add(new HrCancellationRequest
                    {
                        RequestId = request.Id,
                        Name = request.User.Name,
                        Department = request.User.Department == null ? "—" : request.User.Department.Name,
                        Type = request.Type.ToString(),
                        StartDate = request.StartDate,
                        EndDate = request.EndDate,
                        Days = await CountWorkingDaysAsync(hrUser, request.StartDate, request.EndDate),
                        Role = request.User.Role.ToString(),
                        Reason = request.Reason,
                        CancellationReason = request.CancellationReason,
                        RequestedAt = request.CancellationRequestedAt!.Value,
                        InProgress = request.StartDate <= today
                    });
                }
            }

            return result;
        }

        public async Task<LeaveRequest> HrDecideRequestAsync(
            Guid approverId, Guid requestId, bool approve, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(approverId, onBehalf);
            var approver = await _userGateway.GetUserByIdAsync(approverId);
            if (approver == null)
                throw new EntityNotFoundException($"No user with id {approverId}.");

            var request = await _leaveRequestGateway.GetRequestByIdAsync(requestId);
            if (request == null)
                throw new EntityNotFoundException($"No leave request with id {requestId}.");
            if (request.User.RegionId != approver.RegionId)
                throw new UnauthorizedException("You cannot review requests from another region.");
            if (request.Status != LeaveStatus.Pending)
                throw new InvalidOperationException("This request has already been decided.");

            var requirement = LeaveApprovalPolicy.DetermineRequirement(request.User);
            // The HR dashboard's list already excludes these, but the UI can't be trusted
            // to enforce it — e.g. HR staff's own requests route to their manager only.
            if (!requirement.NeedsHrApproval)
                throw new UnauthorizedException("This request does not require HR approval.");
            if (request.Approvals.Any(a => a.Step == LeaveApproval.HrApprovalStep))
                throw new InvalidOperationException("HR has already decided this request.");

            var approval = new LeaveApproval
            {
                LeaveRequestId = request.Id,
                ApproverId = approverId,
                Step = LeaveApproval.HrApprovalStep,
                Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected,
                ReviewedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            request.Approvals.Add(approval);

            // A reject is final immediately — no reason to make the manager review a
            // doomed request. An approve only finalizes once every required approver
            // (the manager, if this request needs one) has also approved.
            var isFinal = !approve || LeaveApprovalPolicy.IsFullyApproved(request, requirement);
            if (isFinal)
                request.Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;

            await _leaveRequestGateway.SaveDecisionAsync(request, approval);

            var period = request.StartDate.ToString("MMM d", CultureInfo.InvariantCulture)
                         + " – " +
                         request.EndDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

            await RecordDelegatedActionAsync(
                delegation, approverId, request.UserId,
                approve ? DelegatedActionType.LeaveApproved : DelegatedActionType.LeaveRejected,
                request.Id, $"{request.Type} leave, {period}");

            try
            {
                var actor = ActorLabel(approver, delegation);
                string notificationMessage;
                if (isFinal)
                {
                    notificationMessage = $"Your {request.Type} leave request for {period} was {(request.Status == LeaveStatus.Approved ? "approved" : "declined")} by {actor}.";
                }
                else
                {
                    notificationMessage = $"Your {request.Type} leave request for {period} was approved by {actor} and is now awaiting Manager approval.";
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

            _logger.LogInformation("HR/Delegate {ApproverId} {Decision} leave request {RequestId}{Final}.",
                approverId, approve ? "approved" : "rejected", requestId, isFinal ? "" : " (still awaiting manager)");

            return request;
        }

        // Asks HR to undo leave that was already approved. Nothing about the request changes
        // yet — the days stay spent and the leave stays Approved until HR decides, because a
        // request that might be refused must not free the balance in the meantime.
        public async Task<LeaveRequest> RequestCancellationAsync(
            Guid userId, Guid requestId, string reason, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(userId, onBehalf);

            var requester = await _userGateway.GetUserByIdAsync(userId);
            if (requester == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            // HR is being asked to overturn a decision, so they need to know why.
            if (string.IsNullOrWhiteSpace(reason))
                throw new InvalidOperationException("A reason is required to request a cancellation.");

            var request = await _leaveRequestGateway.GetRequestByIdAsync(requestId);
            if (request == null)
                throw new EntityNotFoundException($"No leave request with id {requestId}.");
            if (request.UserId != userId)
                throw new InvalidOperationException("You can only cancel your own requests.");

            // A Pending request needs no one's permission to withdraw — that is CancelRequestAsync.
            if (request.Status != LeaveStatus.Approved)
                throw new InvalidOperationException(
                    "Only approved leave can be sent to HR for cancellation.");
            if (request.CancellationRequestedAt is not null)
                throw new InvalidOperationException(
                    "HR is already reviewing a cancellation for this request.");

            // Leave that is over cannot be given back.
            if (request.EndDate < DateOnly.FromDateTime(DateTime.Today))
                throw new InvalidOperationException("This leave has already ended.");

            request.CancellationRequestedAt = DateTime.UtcNow;
            request.CancellationReason = reason.Trim();
            await _leaveRequestGateway.CancelRequestAsync(request);

            var period = Period(request.StartDate, request.EndDate);

            await RecordDelegatedActionAsync(
                delegation, userId, userId,
                DelegatedActionType.LeaveCancellationRequested,
                request.Id, $"{request.Type} leave, {period}");

            // Best effort, like every other notification here: the request is the thing that
            // must survive, and HR sees it on the dashboard whether or not this lands.
            try
            {
                // The requester gets a receipt for their own action, in the second person, and
                // pointed at their own requests. Without this an HR employee cancelling their
                // own leave landed in their own recipient list and was told, in the third
                // person, that they had asked — reading as somebody else's request to action.
                await _notifications.SendNotificationAsync(
                    userId,
                    $"You asked HR to cancel your approved {request.Type} leave for {period}. "
                        + $"Reason: {request.CancellationReason}",
                    "/employee/my-requests");

                foreach (var approver in await HrStaffInRegionAsync(requester.RegionId))
                {
                    if (approver.Id == userId)
                        continue;

                    await _notifications.SendNotificationAsync(
                        approver.Id,
                        $"{requester.Name} asked to cancel approved {request.Type} leave for "
                            + $"{period}. Reason: {request.CancellationReason}",
                        "/hr/dashboard");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Cancellation request on {RequestId} saved but notifying HR failed.", requestId);
            }

            _logger.LogInformation(
                "User {UserId} asked HR to cancel approved leave request {RequestId}.",
                userId, requestId);

            return request;
        }

        // HR's answer to the above. Approving is what finally frees the days: the balance
        // counts Approved rows, so flipping the status to Cancelled is the whole of it.
        public async Task<LeaveRequest> HrDecideCancellationAsync(
            Guid hrUserId, Guid requestId, bool approve, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(hrUserId, onBehalf);

            var hrUser = await _userGateway.GetUserByIdAsync(hrUserId);
            if (hrUser == null)
                throw new EntityNotFoundException($"No user with id {hrUserId}.");

            // The dashboard gates on the Department claim, but a claim is not a control —
            // the route is reachable by URL and the cookie outlives a transfer out of HR.
            if (hrUser.Department?.Name != LeaveApprovalPolicy.HrDepartmentName)
                throw new UnauthorizedException("Only HR can decide a cancellation request.");

            var request = await _leaveRequestGateway.GetRequestByIdAsync(requestId);
            if (request == null)
                throw new EntityNotFoundException($"No leave request with id {requestId}.");

            // Looking is worldwide, acting is regional.
            if (request.User.RegionId != hrUser.RegionId)
                throw new UnauthorizedException("You cannot review requests from another region.");

            if (request.CancellationRequestedAt is null)
                throw new InvalidOperationException("Nobody has asked to cancel this request.");
            if (request.Status != LeaveStatus.Approved)
                throw new InvalidOperationException("This request is no longer approved.");

            var employeeReason = request.CancellationReason;

            if (approve)
            {
                request.Status = LeaveStatus.Cancelled;
            }
            else
            {
                // Back to plain approved leave, so the employee can ask again if they need to.
                request.CancellationRequestedAt = null;
                request.CancellationReason = null;
            }

            await _leaveRequestGateway.CancelRequestAsync(request);

            var period = Period(request.StartDate, request.EndDate);

            await RecordDelegatedActionAsync(
                delegation, hrUserId, request.UserId,
                approve
                    ? DelegatedActionType.LeaveCancellationApproved
                    : DelegatedActionType.LeaveCancellationRejected,
                request.Id, $"{request.Type} leave, {period}");

            try
            {
                var actor = ActorLabel(hrUser, delegation);
                var message = approve
                    ? $"Your request to cancel {request.Type} leave for {period} was approved by "
                        + $"{actor}. The days have been returned to your balance."
                    : $"Your request to cancel {request.Type} leave for {period} was declined by "
                        + $"{actor}. The leave stands.";

                await _notifications.SendNotificationAsync(
                    request.UserId, message, "/employee/my-requests");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Cancellation decision on {RequestId} saved but the notification failed.", requestId);
            }

            _logger.LogInformation(
                "HR/Delegate {HrUserId} {Decision} the cancellation of leave request {RequestId} (employee reason: {Reason}).",
                hrUserId, approve ? "approved" : "declined", requestId, employeeReason);

            return request;
        }

        // HR decides cancellations for anyone, so the notification goes to the HR department
        // in the requester's own region — acting stays regional even though looking does not.
        // Deliberately not narrowed by LeaveApprovalPolicy: that routes the *original*
        // request, and an approved cancellation is HR's call regardless of who approved first.
        private async Task<List<User>> HrStaffInRegionAsync(Guid regionId)
        {
            var staff = new List<User>();
            foreach (var user in await _userGateway.GetAllUsersAsync())
            {
                if (user.Status == UserStatus.Active
                    && user.RegionId == regionId
                    && user.Department?.Name == LeaveApprovalPolicy.HrDepartmentName)
                    staff.Add(user);
            }
            return staff;
        }


        public async Task<LeaveRequest> SubmitRequestAsync(
            Guid userId, LeaveType type, DateOnly start, DateOnly end, string? reason,
            ActingOnBehalf? onBehalf = null, bool allowPastDates = false)
        {
            var delegation = await GuardAsync(userId, onBehalf);

            var today = DateOnly.FromDateTime(DateTime.Today);

            if (end < start)
                throw new InvalidOperationException("End date must not be before start date.");
            if (start < today && !allowPastDates)
                throw new InvalidOperationException("Leave cannot start in the past.");

            var requester = await _userGateway.GetUserByIdAsync(userId);
            if (requester == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            var existing = await _leaveRequestGateway.GetRequestsByUserAsync(userId);
            var overlaps = existing.Any(r =>
                (r.Status == LeaveStatus.Pending || r.Status == LeaveStatus.Approved)
                && r.StartDate <= end
                && r.EndDate >= start);
            if (overlaps)
                throw new InvalidOperationException("You already have a request in that period.");

            await EnsureWorkingDayAsync(requester, start);
            await EnsureWorkingDayAsync(requester, end);
            var requestedDays = await CountWorkingDaysAsync(requester, start, end);
            if (requestedDays == 0)
                throw new InvalidOperationException("The selected period contains no working days.");
            var balances = await GetMyBalancesAsync(userId, start.Year, start);
            var balance = balances.FirstOrDefault(b => b.Type == type);
            if (balance == null || balance.DaysRemaining < requestedDays)
                throw new InvalidOperationException("Not enough days left for this leave type.");

            // Admins sit outside the approval workflow entirely (no approve/reject UI
            // exists for them as either requester's manager or reviewer) — auto-approved.
            var requirement = LeaveApprovalPolicy.DetermineRequirement(requester);

            // Nobody reviews an admin's leave, so the only thing standing between them and
            // an unattended account is this: someone has to be covering before the request
            // is created. Overlap is enough — the cover may be shorter than the leave.
            if (requester.Role == UserRole.Admin
                && !await _delegationGateway.HasActiveDelegationInPeriodAsync(userId, start, end))
            {
                throw new DelegationRequiredException(
                    "Choose a colleague to take over your responsibilities before requesting this leave.");
            }

            var request = new LeaveRequest
            {
                UserId = userId,
                Type = type,
                StartDate = start,
                EndDate = end,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                Status = requirement.AutoApproved ? LeaveStatus.Approved : LeaveStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            await _leaveRequestGateway.CreateRequestAsync(request);

            await RecordDelegatedActionAsync(
                delegation, userId, userId, DelegatedActionType.LeaveRequested, request.Id,
                $"{type} leave {Period(start, end)}");

            await TryWarnManagerAboutLowAvailabilityAsync(requester, request);

            _logger.LogInformation("User {UserId} submitted a {Type} leave request {Start}–{End}{AutoApproved}.",
                userId, type, start, end, requirement.AutoApproved ? " (auto-approved)" : "");
            return request;
        }

        private async Task TryWarnManagerAboutLowAvailabilityAsync(
            User requester,
            LeaveRequest submittedRequest)
        {
            try
            {
                await WarnManagerAboutLowAvailabilityAsync(requester, submittedRequest);
            }
            catch (Exception exception)
            {
                // The request is already saved. Conflict notifications are best-effort.
                _logger.LogWarning(exception,
                    "Could not evaluate or send a low-availability warning for request {RequestId}.",
                    submittedRequest.Id);
            }
        }

        private async Task WarnManagerAboutLowAvailabilityAsync(
            User requester,
            LeaveRequest submittedRequest)
        {
            if (requester.ManagerId is not { } managerId)
                return;

            var manager = await _userGateway.GetUserByIdAsync(managerId);
            if (manager == null
                || manager.Role != UserRole.LineManager
                || manager.RegionId != requester.RegionId)
                return;

            var team = (await _userGateway.GetDirectReportsAsync(managerId))
                .Where(member => member.RegionId == manager.RegionId)
                .ToList();
            if (team.Count == 0)
                return;

            var requests = await _leaveRequestGateway.GetActiveRequestsForUsersAsync(
                team.Select(member => member.Id).ToList(),
                submittedRequest.StartDate,
                submittedRequest.EndDate);

            var holidays = new HashSet<DateOnly>();
            for (var year = submittedRequest.StartDate.Year;
                 year <= submittedRequest.EndDate.Year;
                 year++)
            {
                foreach (var holiday in await _holidayProvider!.GetHolidaysAsync(
                             requester.Region.Code, year))
                    holidays.Add(holiday.Date);
            }

            var warningDates = new List<DateOnly>();
            var maximumUnavailable = 0;
            for (var day = submittedRequest.StartDate;
                 day <= submittedRequest.EndDate;
                 day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                    || holidays.Contains(day))
                    continue;

                var unavailable = requests
                    .Where(request => request.StartDate <= day && request.EndDate >= day)
                    .Select(request => request.UserId)
                    .Distinct()
                    .Count();

                if (!TeamAvailabilityPolicy.IsBelowMinimum(team.Count, unavailable))
                    continue;

                warningDates.Add(day);
                maximumUnavailable = Math.Max(maximumUnavailable, unavailable);
            }

            if (warningDates.Count == 0)
                return;

            var firstDate = warningDates[0];
            var lastDate = warningDates[^1];
            var availability = TeamAvailabilityPolicy.AvailabilityPercent(
                team.Count, maximumUnavailable);
            var period = firstDate == lastDate
                ? $"on {firstDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"
                : $"from {firstDate.ToString("MMM d", CultureInfo.InvariantCulture)} "
                  + $"to {lastDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
            var message = $"Low team availability: only {availability}% of the team is available {period}. "
                + $"{maximumUnavailable} of {team.Count} members have pending or approved leave. "
                + "Review before approving.";

            await _notifications.SendNotificationAsync(managerId, message, "/manager/team");
        }

        public async Task UpdateRequestDatesAsync(Guid requestId, DateOnly newStart, DateOnly newEnd)
        {
            var request = await _leaveRequestGateway.GetRequestByIdAsync(requestId);
            if (request == null)
                throw new EntityNotFoundException($"No leave request with id {requestId}.");
            if (request.Status != LeaveStatus.Pending)
                throw new InvalidOperationException("Only pending requests can be edited.");
            if (newEnd < newStart)
                throw new InvalidOperationException("End date must not be before start date.");

            var existing = await _leaveRequestGateway.GetRequestsByUserAsync(request.UserId);
            var overlaps = existing.Any(r =>
                r.Id != requestId
                && (r.Status == LeaveStatus.Pending || r.Status == LeaveStatus.Approved)
                && r.StartDate <= newEnd
                && r.EndDate >= newStart);
            if (overlaps)
                throw new InvalidOperationException("This user already has a request in that period.");

            // Reload through the user gateway so the employee's Region navigation is
            // always available when regional working-day rules are evaluated.
            var requester = await _userGateway.GetUserByIdAsync(request.UserId);
            if (requester == null)
                throw new EntityNotFoundException($"No user with id {request.UserId}.");

            await EnsureWorkingDayAsync(requester, newStart);
            await EnsureWorkingDayAsync(requester, newEnd);
            var requestedDays = await CountWorkingDaysAsync(requester, newStart, newEnd);
            var balances = await GetMyBalancesAsync(request.UserId, newStart.Year, newStart);
            var balance = balances.FirstOrDefault(b => b.Type == request.Type);
            if (balance == null || balance.DaysRemaining < requestedDays)
                throw new InvalidOperationException("Not enough days left for this leave type.");

            request.StartDate = newStart;
            request.EndDate = newEnd;
            await _leaveRequestGateway.UpdateRequestDatesAsync(request);
            await TryWarnManagerAboutLowAvailabilityAsync(requester, request);

            _logger.LogInformation("Leave request {RequestId} dates updated to {Start}–{End}.",
                requestId, newStart, newEnd);
        }

        // Lets the requester withdraw their own request while it is still Pending — i.e.
        // before anyone (manager or HR) has acted on it. Once a request is Approved or
        // Rejected it is no longer eligible: the decision has already been made.
        public async Task CancelRequestAsync(Guid userId, Guid requestId, string? reason)
        {
            var request = await _leaveRequestGateway.GetRequestByIdAsync(requestId);
            if (request == null)
                throw new EntityNotFoundException($"No leave request with id {requestId}.");
            if (request.UserId != userId)
                throw new InvalidOperationException("You can only cancel your own requests.");
            if (request.Status != LeaveStatus.Pending)
                throw new InvalidOperationException(
                    "Only requests nobody has approved yet can be cancelled.");

            request.Status = LeaveStatus.Cancelled;
            request.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            await _leaveRequestGateway.CancelRequestAsync(request);

            _logger.LogInformation("Leave request {RequestId} cancelled by its owner.", requestId);
        }

        // "Mar 3 – Mar 14, 2026". Invariant on purpose: audit rows are read by whoever opens
        // the history, in whatever language, and must not shift meaning with the server locale.
        private static string Period(DateOnly start, DateOnly end) =>
            start.ToString("MMM d", CultureInfo.InvariantCulture)
            + " – "
            + end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
