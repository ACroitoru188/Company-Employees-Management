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
    public class EmployeeContext : BaseContext
    {
        private readonly ILeaveRequestGateway _leaveRequestGateway;
        private readonly IUserGateway _userGateway;
        private readonly IDepartmentGateway _departmentGateway;
        private readonly IContractGateway _contractGateway;
        private readonly NotificationContext _notifications;

        public EmployeeContext(
            ILogger<EmployeeContext> logger,
            ILeaveRequestGateway leaveRequestGateway,
            IUserGateway userGateway,
            IDepartmentGateway departmentGateway,
            IContractGateway contractGateway,
            NotificationContext notifications) : base(logger)
        {
            _leaveRequestGateway = leaveRequestGateway;
            _userGateway = userGateway;
            _departmentGateway = departmentGateway;
            _contractGateway = contractGateway;
            _notifications = notifications;
        }

        public async Task<User> GetEmployeeByEmailAsync(string email)
        {
            var user = await _userGateway.GetUserByEmailAsync(email);
            if (user == null)
                throw new EntityNotFoundException($"No user with email {email}.");
            return user;
        }

        public Task<List<LeaveRequest>> GetMyRequestsAsync(Guid userId)
        {
            return _leaveRequestGateway.GetRequestsByUserAsync(userId);
        }

        public async Task<List<LeaveBalanceResult>> GetMyBalancesAsync(Guid userId, int year)
        {
            var allocations = await _leaveRequestGateway.GetAllocationsByUserAsync(userId, year);
            var requests = await _leaveRequestGateway.GetRequestsByUserAsync(userId);

            var balances = new List<LeaveBalanceResult>();
            foreach (var allocation in allocations)
            {
                var daysUsed = requests
                    .Where(r => r.Status == LeaveStatus.Approved
                                && r.Type == allocation.LeaveType
                                && r.StartDate.Year == year)
                    .Sum(r => CountDays(r.StartDate, r.EndDate));

                balances.Add(new LeaveBalanceResult
                {
                    Type = allocation.LeaveType,
                    DaysTotal = allocation.NumberOfDays,
                    DaysUsed = daysUsed
                });
            }
            return balances;
        }

        // Approved leave of the user's team (same manager, excluding the user,
        // plus the manager themself) that touches the [from, to] interval.
        public async Task<List<LeaveRequest>> GetTeamRequestsAsync(Guid userId, DateOnly from, DateOnly to)
        {
            var team = await GetTeamMembersAsync(userId);

            var teamIds = new List<Guid>();
            foreach (var member in team)
            {
                teamIds.Add(member.Id);
            }

            if (teamIds.Count == 0)
                return [];

            return await _leaveRequestGateway.GetApprovedRequestsForUsersAsync(teamIds, from, to);
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
            if (manager != null)
                team.Add(manager);

            var allUsers = await _userGateway.GetAllUsersAsync();
            foreach (var user in allUsers)
            {
                if (user.ManagerId == me.ManagerId && user.Id != userId && user.Status == UserStatus.Active)
                    team.Add(user);
            }
            return team;
        }

        // Org-wide figures for the HR dashboard. One call, so the page issues a
        // single round trip instead of several against the same scoped context.
        public async Task<HrDashboardResult> GetHrDashboardAsync()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var result = new HrDashboardResult();

            var users = await _userGateway.GetAllUsersAsync();
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

            var pending = await _leaveRequestGateway.GetAllPendingRequestsAsync();
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
                    Days = request.EndDate.DayNumber - request.StartDate.DayNumber + 1,
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
            }

            return result;
        }

        public async Task<LeaveRequest> HrDecideRequestAsync(Guid approverId, Guid requestId, bool approve)
        {
            var request = await _leaveRequestGateway.GetRequestByIdAsync(requestId);
            if (request == null)
                throw new EntityNotFoundException($"No leave request with id {requestId}.");
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
                    notificationMessage = $"Your {request.Type} leave request for {period} was approved by HR and is now awaiting Manager approval.";
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

            _logger.LogInformation("HR {ApproverId} {Decision} leave request {RequestId}{Final}.",
                approverId, approve ? "approved" : "rejected", requestId, isFinal ? "" : " (still awaiting manager)");

            return request;
        }

        // --- administrare departamente (folosit de pagina admin crud) -----------------

        public Task<List<Department>> GetDepartmentsAsync() =>
            _departmentGateway.GetAllAsync();

        public Task<List<User>> GetAllUsersAsync() =>
            _userGateway.GetAllUsersAsync();

        public async Task<Department> CreateDepartmentAsync(string name, Guid? managerId)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Department name is required.");

            var department = new Department { Name = name.Trim(), ManagerId = managerId };
            await _departmentGateway.CreateAsync(department);
            return department;
        }

        public async Task UpdateDepartmentAsync(Guid id, string name, Guid? managerId)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Department name is required.");

            var department = await _departmentGateway.GetByIdAsync(id);
            if (department == null)
                throw new EntityNotFoundException($"No department with id {id}.");

            department.Name = name.Trim();
            department.ManagerId = managerId;
            await _departmentGateway.UpdateAsync(department);
        }

        public Task DeleteDepartmentAsync(Guid id) =>
            _departmentGateway.DeleteAsync(id);

        public async Task AssignUserToDepartmentAsync(Guid userId, Guid? departmentId)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            user.DepartmentId = departmentId;
            await _userGateway.UpdateUserAsync(user);
        }

        public async Task<LeaveRequest> SubmitRequestAsync(
            Guid userId, LeaveType type, DateOnly start, DateOnly end, string? reason)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            if (end < start)
                throw new InvalidOperationException("End date must not be before start date.");
            if (start < today)
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

            var requestedDays = CountDays(start, end);
            var balances = await GetMyBalancesAsync(userId, start.Year);
            var balance = balances.FirstOrDefault(b => b.Type == type);
            if (balance == null || balance.DaysTotal - balance.DaysUsed < requestedDays)
                throw new InvalidOperationException("Not enough days left for this leave type.");

            // Admins sit outside the approval workflow entirely (no approve/reject UI
            // exists for them as either requester's manager or reviewer) — auto-approved.
            var requirement = LeaveApprovalPolicy.DetermineRequirement(requester);

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

            _logger.LogInformation("User {UserId} submitted a {Type} leave request {Start}–{End}{AutoApproved}.",
                userId, type, start, end, requirement.AutoApproved ? " (auto-approved)" : "");
            return request;
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

            var requestedDays = CountDays(newStart, newEnd);
            var balances = await GetMyBalancesAsync(request.UserId, newStart.Year);
            var balance = balances.FirstOrDefault(b => b.Type == request.Type);
            if (balance == null || balance.DaysTotal - balance.DaysUsed < requestedDays)
                throw new InvalidOperationException("Not enough days left for this leave type.");

            request.StartDate = newStart;
            request.EndDate = newEnd;
            await _leaveRequestGateway.UpdateRequestDatesAsync(request);

            _logger.LogInformation("Leave request {RequestId} dates updated to {Start}–{End}.",
                requestId, newStart, newEnd);
        }

        private static int CountDays(DateOnly start, DateOnly end) =>
            end.DayNumber - start.DayNumber + 1;

        public async Task<OrgChartNode?> GetCompanyOrgChartAsync(Guid currentUserId, bool isAdmin)
        {
            var allUsers = await _userGateway.GetAllUsersAsync();
            var activeUsers = allUsers.Where(u => u.Status == UserStatus.Active).ToList();
            var allPendingRequests = await _leaveRequestGateway.GetAllCompanyPendingRequestsAsync();

            var nodeMap = new Dictionary<Guid, OrgChartNode>();
            foreach (var u in activeUsers)
            {
                var initials = string.Concat(u.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[0].ToString())).ToUpperInvariant();
                if (initials.Length > 2) initials = initials.Substring(0, 2);

                var activeContract = u.Contracts?.FirstOrDefault(c => c.Status == ContractStatus.Active);
                var pendingReq = allPendingRequests.FirstOrDefault(r => r.UserId == u.Id);

                nodeMap[u.Id] = new OrgChartNode
                {
                    UserId = u.Id,
                    Name = u.Name,
                    Email = u.Email ?? string.Empty,
                    Role = u.Role.ToString(),
                    Department = u.Department?.Name ?? string.Empty,
                    Initials = initials,
                    ManagerId = u.ManagerId,
                    HasPendingRequest = pendingReq != null,
                    PendingRequestId = pendingReq?.Id,
                    PendingRequestType = pendingReq?.Type.ToString(),
                    PendingRequestDates = pendingReq != null ? $"{pendingReq.StartDate:MMM d} – {pendingReq.EndDate:MMM d, yyyy}" : null,
                    HasContract = activeContract != null,
                    ContractId = activeContract?.Id,
                    ContractType = activeContract?.Type,
                    ContractStatus = activeContract?.Status,
                    ContractStartDate = activeContract?.StartDate,
                    ContractEndDate = activeContract?.EndDate
                };
            }

            // Cycle detection using upward traversal
            foreach (var user in activeUsers)
            {
                var visited = new HashSet<Guid>();
                var currentId = user.Id;
                while (currentId != Guid.Empty)
                {
                    if (!visited.Add(currentId))
                    {
                        _logger.LogError("Cycle detected in org chart involving user {UserId}", currentId);
                        throw new InvalidOperationException("Hierarchy cycle detected in database.");
                    }
                    var current = activeUsers.FirstOrDefault(u => u.Id == currentId);
                    if (current?.ManagerId == null)
                        break;
                    currentId = current.ManagerId.Value;
                }
            }

            // Build tree
            var childrenMap = new Dictionary<Guid, List<OrgChartNode>>();
            var root = new OrgChartNode
            {
                UserId = Guid.Empty,
                Name = "Company",
                Role = "Headquarters",
                Department = "All Departments",
                Initials = "HQ"
            };

            foreach (var user in activeUsers)
            {
                var node = nodeMap[user.Id];
                if (user.ManagerId == null)
                {
                    if (!childrenMap.ContainsKey(Guid.Empty))
                        childrenMap[Guid.Empty] = new List<OrgChartNode>();
                    childrenMap[Guid.Empty].Add(node);
                }
                else
                {
                    if (!childrenMap.ContainsKey(user.ManagerId.Value))
                        childrenMap[user.ManagerId.Value] = new List<OrgChartNode>();
                    childrenMap[user.ManagerId.Value].Add(node);
                }
            }

            // Recursive function to attach children
            void AttachChildren(OrgChartNode parent)
            {
                if (childrenMap.TryGetValue(parent.UserId, out var children))
                {
                    parent.Subordinates = children.OrderBy(c => c.Name).ToList();
                    foreach (var child in parent.Subordinates)
                    {
                        AttachChildren(child);
                    }
                }
            }

            if (root != null)
            {
                AttachChildren(root);
                
                // Determine focus
                if (!isAdmin)
                {
                    var currentUser = activeUsers.FirstOrDefault(u => u.Id == currentUserId);
                    if (currentUser != null)
                    {
                        // Mark current user's department as focus
                        MarkFocus(root, currentUser.Department?.Name);
                    }
                }
                else
                {
                    MarkFocus(root, null); // Admins see everything focused (or nothing faded)
                }

                // Math Layout Passes
                CalculateSubtreeWidths(root);
                CalculateNodeCoordinates(root, 0, root.SubtreeWidth / 2, 0, 50.0);
            }

            return root;
        }

        private void CalculateSubtreeWidths(OrgChartNode node)
        {
            if (node.Subordinates.Count == 0)
            {
                node.SubtreeWidth = 80.0; // Base width for a single node
                return;
            }

            double totalWidth = 0;
            foreach (var child in node.Subordinates)
            {
                CalculateSubtreeWidths(child);
                totalWidth += child.SubtreeWidth;
            }

            node.SubtreeWidth = Math.Max(80.0, totalWidth);
        }

        private void CalculateNodeCoordinates(OrgChartNode node, int depth, double x, int siblingIndex, double currentY)
        {
            node.Depth = depth;
            node.X = x;
            
            // Stagger siblings to save space (odd indices are 40px lower)
            // The stagger is added to the current accumulated Y, so the whole subtree shifts down.
            double stagger = (siblingIndex % 2 == 1) ? 40.0 : 0.0;
            node.Y = currentY + stagger;

            if (node.Subordinates.Count > 0)
            {
                double currentX = x - (node.SubtreeWidth / 2.0);
                for (int i = 0; i < node.Subordinates.Count; i++)
                {
                    var child = node.Subordinates[i];
                    double childCenter = currentX + (child.SubtreeWidth / 2.0);
                    // Next level base Y is node.Y + 120
                    CalculateNodeCoordinates(child, depth + 1, childCenter, i, node.Y + 120.0);
                    currentX += child.SubtreeWidth;
                }
            }
        }

        private void MarkFocus(OrgChartNode node, string? targetDepartment)
        {
            if (targetDepartment == null) 
            {
                node.IsFocusNode = true;
            }
            else
            {
                node.IsFocusNode = node.Department == targetDepartment;
            }
            
            foreach (var child in node.Subordinates)
            {
                MarkFocus(child, targetDepartment);
            }
        }

        public async Task<Contract?> GetActiveContractForUserAsync(Guid userId)
        {
            return await _contractGateway.GetActiveContractByUserIdAsync(userId);
        }

        public async Task SaveUserContractAsync(
            Guid userId,
            ContractType type,
            ContractStatus status,
            DateOnly startDate,
            DateOnly? endDate,
            string? notes)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            var active = await _contractGateway.GetActiveContractByUserIdAsync(userId);
            if (active != null)
            {
                active.Type = type;
                active.Status = status;
                active.StartDate = startDate;
                active.EndDate = type == ContractType.Indeterminate ? null : endDate;
                active.Notes = notes;
                active.UpdatedAt = DateTime.UtcNow;
                await _contractGateway.UpdateAsync(active);
            }
            else
            {
                var newContract = new Contract
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Type = type,
                    Status = status,
                    StartDate = startDate,
                    EndDate = type == ContractType.Indeterminate ? null : endDate,
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _contractGateway.CreateAsync(newContract);
            }
        }
    }
}
