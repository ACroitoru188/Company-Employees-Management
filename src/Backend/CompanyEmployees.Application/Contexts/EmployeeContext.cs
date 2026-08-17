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
        private readonly IRegionGateway _regionGateway;
        private readonly IPublicHolidayProvider _holidayProvider;
        private readonly IContractGateway _contractGateway;
        private readonly IManagerDelegationGateway _delegationGateway;
        private readonly NotificationContext _notifications;

        public EmployeeContext(
            ILogger<EmployeeContext> logger,
            ILeaveRequestGateway leaveRequestGateway,
            IUserGateway userGateway,
            IDepartmentGateway departmentGateway,
            IRegionGateway regionGateway,
            IPublicHolidayProvider holidayProvider,
            IContractGateway contractGateway,
            IManagerDelegationGateway delegationGateway,
            NotificationContext notifications) : base(logger)
        {
            _leaveRequestGateway = leaveRequestGateway;
            _userGateway = userGateway;
            _departmentGateway = departmentGateway;
            _regionGateway = regionGateway;
            _holidayProvider = holidayProvider;
            _contractGateway = contractGateway;
            _delegationGateway = delegationGateway;
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
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            await _leaveRequestGateway.EnsureDefaultAllocationsAsync(userId, year);
            var allocations = await _leaveRequestGateway.GetAllocationsByUserAsync(userId, year);
            var requests = await _leaveRequestGateway.GetRequestsByUserAsync(userId);
            var holidays = (await _holidayProvider.GetHolidaysAsync(user.Region.Code, year))
                .Select(holiday => holiday.Date)
                .ToHashSet();

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
                    DaysTotal = allocation.NumberOfDays,
                    DaysUsed = daysUsed
                });
            }
            return balances;
        }

        public async Task<IReadOnlyList<PublicHoliday>> GetRegionalHolidaysAsync(Guid userId, int year)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            return await _holidayProvider.GetHolidaysAsync(user.Region.Code, year);
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

        public async Task<List<OrgChartNode>> GetOrgChartChildrenAsync(OrgChartNode parentNode, Guid currentUserId, bool isAdmin)
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
                    ContractEndDate = activeContract?.EndDate,
                    HasUnloadedChildren = activeUsers.Any(child => child.ManagerId == u.Id),
                    IsExpanded = false
                };
            }

            var subordinates = new List<OrgChartNode>();

            if (parentNode.Role == "City")
            {
                var siteNode1 = new OrgChartNode
                {
                    UserId = Guid.NewGuid(),
                    Name = "Siemens Industry Software Center",
                    Role = "Site",
                    Department = "Region",
                    Initials = "SI",
                    ManagerId = parentNode.UserId,
                    HasUnloadedChildren = true,
                    IsExpanded = false
                };
                var siteNode2 = new OrgChartNode
                {
                    UserId = Guid.NewGuid(),
                    Name = "Siemens R&D Advanta Center",
                    Role = "Site",
                    Department = "Region",
                    Initials = "SR",
                    ManagerId = parentNode.UserId,
                    HasUnloadedChildren = true,
                    IsExpanded = false
                };
                subordinates.Add(siteNode1);
                subordinates.Add(siteNode2);
            }
            else if (parentNode.Role == "Site")
            {
                // Return all admins in the system (or filtered by region if preferred)
                var adminUsers = activeUsers.Where(u => u.Role == UserRole.Admin).OrderBy(u => u.Name).ToList();
                // Split them based on Department to allow specific assignment
                if (parentNode.Name == "Siemens Industry Software Center")
                {
                    adminUsers = adminUsers.Where(u => u.Department?.Name == "Industry Software").ToList();
                }
                else
                {
                    // Fallback for Advanta: everyone else (existing demo users)
                    adminUsers = adminUsers.Where(u => u.Department?.Name != "Industry Software").ToList();
                }

                foreach (var u in adminUsers)
                {
                    var node = nodeMap[u.Id];
                    node.HasUnloadedChildren = true;
                    node.IsExpanded = false;
                    // Site is artificial, so we override ManagerId to render correctly under Site
                    node.ManagerId = parentNode.UserId;
                    subordinates.Add(node);
                }
            }
            else if (parentNode.Role == "Department")
            {
                // For CountryManager's admins, their ManagerId was overridden to Site's UserId in the UI memory.
                // However, in DB, the Admin's ManagerId is still HQ (null) or someone else.
                // But activeUsers will match if we use the original logic if we look at real managers.
                // Wait, if parentNode.Role == "Department", the parentNode.ManagerId is the Admin's UserId!
                var adminSubordinates = activeUsers.Where(u => u.ManagerId == parentNode.ManagerId).ToList();
                var deptUsers = adminSubordinates.Where(u => (u.Department?.Name ?? "No Department") == parentNode.Department).ToList();
                
                foreach (var u in deptUsers)
                {
                    subordinates.Add(nodeMap[u.Id]);
                }
            }
            else if (parentNode.Role == "Admin")
            {
                var children = activeUsers.Where(u => u.ManagerId == parentNode.UserId).ToList();
                var deptGroups = children.GroupBy(c => string.IsNullOrWhiteSpace(c.Department?.Name) ? "No Department" : c.Department.Name).OrderBy(g => g.Key);
                foreach (var group in deptGroups)
                {
                    var deptName = group.Key;
                    var deptNode = new OrgChartNode
                    {
                        UserId = Guid.NewGuid(),
                        Name = deptName,
                        Role = "Department",
                        Department = deptName,
                        Initials = deptName.Length >= 2 ? deptName.Substring(0, 2).ToUpperInvariant() : "DP",
                        ManagerId = parentNode.UserId,
                        HasUnloadedChildren = true,
                        IsExpanded = false
                    };
                    subordinates.Add(deptNode);
                }
            }
            else
            {
                var children = activeUsers.Where(u => u.ManagerId == parentNode.UserId).ToList();
                foreach (var u in children)
                {
                    subordinates.Add(nodeMap[u.Id]);
                }
            }

            return subordinates.OrderBy(c => c.Name).ToList();
        }

        public async Task<List<OrgChartNode>> GetOrgChartPathAsync(Guid targetUserId, bool isAdmin, Guid currentUserId)
        {
            var allUsers = await _userGateway.GetAllUsersAsync();
            var activeUsers = allUsers.Where(u => u.Status == UserStatus.Active).ToList();
            
            var target = activeUsers.FirstOrDefault(u => u.Id == targetUserId);
            if (target == null) return new List<OrgChartNode>();

            // Find the anchor (same logic as GetCompanyOrgChartAsync)
            Guid anchorId = Guid.Empty;
            if (!isAdmin)
            {
                var currentUser = activeUsers.FirstOrDefault(u => u.Id == currentUserId);
                if (currentUser != null)
                {
                    bool hasSubordinates = activeUsers.Any(u => u.ManagerId == currentUser.Id);
                    anchorId = (hasSubordinates || currentUser.ManagerId == null) ? currentUser.Id : currentUser.ManagerId.Value;
                }
            }

            var chain = new List<User>();
            var curr = target;
            while (curr != null)
            {
                chain.Add(curr);
                if (curr.Id == anchorId) break; // Reached the root of their vision
                if (curr.ManagerId == null) break;
                curr = activeUsers.FirstOrDefault(u => u.Id == curr.ManagerId);
            }
            chain.Reverse();

            var path = new List<OrgChartNode>();
            for (int i = 0; i < chain.Count; i++)
            {
                var u = chain[i];
                var initials = string.Concat(u.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[0].ToString())).ToUpperInvariant();
                if (initials.Length > 2) initials = initials.Substring(0, 2);

                path.Add(new OrgChartNode
                {
                    UserId = u.Id,
                    Name = u.Name,
                    Role = u.Role.ToString(),
                    Department = u.Department?.Name ?? string.Empty,
                    Initials = initials,
                    ManagerId = u.ManagerId
                });

                if (i < chain.Count - 1)
                {
                    var nextUser = chain[i + 1];

                    if (u.Role == UserRole.CountryManager && nextUser.Role == UserRole.Admin)
                    {
                        var cityId = Guid.NewGuid();
                        path.Add(new OrgChartNode
                        {
                            UserId = cityId,
                            Name = "Brașov",
                            Role = "City",
                            Department = "Region",
                            Initials = "BV",
                            ManagerId = u.Id
                        });

                        var isIndustry = nextUser.Department?.Name == "Industry Software";
                        var siteName = isIndustry ? "Siemens Industry Software Center" : "Siemens R&D Advanta Center";
                        var siteInitials = isIndustry ? "SI" : "SR";

                        path.Add(new OrgChartNode
                        {
                            UserId = Guid.NewGuid(),
                            Name = siteName,
                            Role = "Site",
                            Department = "Region",
                            Initials = siteInitials,
                            ManagerId = cityId
                        });
                    }

                    if (u.Role == UserRole.Admin)
                    {
                        var deptName = string.IsNullOrWhiteSpace(nextUser.Department?.Name) ? "No Department" : nextUser.Department.Name;
                        path.Add(new OrgChartNode
                        {
                            UserId = Guid.NewGuid(),
                            Name = deptName,
                            Role = "Department",
                            Department = deptName,
                            Initials = deptName.Length >= 2 ? deptName.Substring(0, 2).ToUpperInvariant() : "DP",
                            ManagerId = u.Id
                        });
                    }
                }
            }

            return path;
        }

        public async Task<List<User>> SearchUsersAsync(string query, int count = 10)
        {
            var allUsers = await _userGateway.GetAllUsersAsync();
            var activeUsers = allUsers.Where(u => u.Status == UserStatus.Active);
            
            return activeUsers
                .Where(u => (!string.IsNullOrWhiteSpace(u.Name) && u.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrWhiteSpace(u.Role.ToString()) && u.Role.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrWhiteSpace(u.Department?.Name) && u.Department.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Take(count)
                .ToList();
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

        public Task<List<Region>> GetRegionsAsync(bool activeOnly = false) =>
            _regionGateway.GetAllAsync(activeOnly);

        public Task<List<User>> GetAllUsersAsync() =>
            _userGateway.GetAllUsersAsync();

        public async Task<List<User>> GetUsersInMyRegionAsync(Guid userId)
        {
            var requester = await _userGateway.GetUserByIdAsync(userId);
            if (requester == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            return (await _userGateway.GetAllUsersAsync())
                .Where(user => user.RegionId == requester.RegionId)
                .ToList();
        }

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

        public async Task AssignUserToDepartmentAsync(Guid adminId, Guid userId, Guid? departmentId)
        {
            var user = await EnsureRegionalAdminCanManageAsync(adminId, userId);

            user.DepartmentId = departmentId;
            await _userGateway.UpdateUserAsync(user);
        }

        public async Task AssignUserToRegionAsync(Guid adminId, Guid userId, Guid regionId)
        {
            var region = await _regionGateway.GetByIdAsync(regionId);
            if (region == null || !region.IsActive)
                throw new InvalidOperationException("Select a valid active region.");

            // The source-region admin owns the transfer. Once the employee moves,
            // only an administrator in the destination region may edit them.
            var user = await EnsureRegionalAdminCanManageAsync(adminId, userId);
            if (user.RegionId == regionId)
                return;

            user.RegionId = regionId;

            // A relocation must not preserve cross-region reporting relationships.
            if (user.ManagerId is Guid managerId)
            {
                var manager = await _userGateway.GetUserByIdAsync(managerId);
                if (manager?.RegionId != regionId)
                    user.ManagerId = null;
            }

            user.SecurityStamp = Guid.NewGuid().ToString("D");
            user.UpdatedAt = DateTime.UtcNow;
            await _userGateway.UpdateUserAsync(user);

            var directReports = await _userGateway.GetAllDirectReportsAsync(userId);
            foreach (var report in directReports.Where(report => report.RegionId != regionId))
            {
                report.ManagerId = null;
                report.UpdatedAt = DateTime.UtcNow;
                await _userGateway.UpdateUserAsync(report);
            }
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

            await EnsureWorkingDayAsync(requester, start);
            await EnsureWorkingDayAsync(requester, end);
            var requestedDays = await CountWorkingDaysAsync(requester, start, end);
            if (requestedDays == 0)
                throw new InvalidOperationException("The selected period contains no working days.");
            var balances = await GetMyBalancesAsync(userId, start.Year);
            var balance = balances.FirstOrDefault(b => b.Type == type);
            if (balance == null || balance.DaysTotal - balance.DaysUsed < requestedDays)
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

            // Reload through the user gateway so the employee's Region navigation is
            // always available when regional working-day rules are evaluated.
            var requester = await _userGateway.GetUserByIdAsync(request.UserId);
            if (requester == null)
                throw new EntityNotFoundException($"No user with id {request.UserId}.");

            await EnsureWorkingDayAsync(requester, newStart);
            await EnsureWorkingDayAsync(requester, newEnd);
            var requestedDays = await CountWorkingDaysAsync(requester, newStart, newEnd);
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

        private async Task EnsureWorkingDayAsync(User user, DateOnly day)
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                throw new InvalidOperationException("Leave must start and end on a working day.");

            var holidays = await _holidayProvider.GetHolidaysAsync(user.Region.Code, day.Year);
            var holiday = holidays.FirstOrDefault(candidate => candidate.Date == day);
            if (holiday != null)
                throw new InvalidOperationException($"{day:MMM d} is {holiday.Name} in {user.Region.Name}.");
        }

        private async Task<int> CountWorkingDaysAsync(User user, DateOnly start, DateOnly end)
        {
            var holidays = new HashSet<DateOnly>();
            for (var year = start.Year; year <= end.Year; year++)
            {
                foreach (var holiday in await _holidayProvider.GetHolidaysAsync(user.Region.Code, year))
                    holidays.Add(holiday.Date);
            }

            return CountWorkingDays(start, end, holidays);
        }

        private static int CountWorkingDays(DateOnly start, DateOnly end, HashSet<DateOnly> holidays)
        {
            var count = 0;
            for (var day = start; day <= end; day = day.AddDays(1))
            {
                if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                    && !holidays.Contains(day))
                {
                    count++;
                }
            }

            return count;
        }

        public async Task<OrgChartNode?> GetCompanyOrgChartAsync(Guid currentUserId, bool isAdmin)
        {
            var allUsers = await _userGateway.GetAllUsersAsync();
            var requestingUser = allUsers.FirstOrDefault(user => user.Id == currentUserId);
            if (requestingUser == null)
                return null; // Gracefully handle stale cookies after DB recreation

            var activeUsers = allUsers
                .Where(user => user.Status == UserStatus.Active
                               && (isAdmin || user.RegionId == requestingUser.RegionId))
                .ToList();
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
                    ContractEndDate = activeContract?.EndDate,
                    Subordinates = new List<OrgChartNode>(),
                    HasUnloadedChildren = false,
                    IsExpanded = false
                };
            }

            // Grouping Functions for New Hierarchy
            OrgChartNode CreateDepartmentNode(Department dept, IEnumerable<User> deptUsers)
            {
                var deptNode = new OrgChartNode { UserId = Guid.NewGuid(), Name = dept.Name, Role = "Department", Department = dept.Name, Initials = dept.Name.Length >= 2 ? dept.Name.Substring(0, 2).ToUpperInvariant() : "DP", IsExpanded = false, Subordinates = new List<OrgChartNode>(), HasUnloadedChildren = false };
                
                // First, assign subordinates for EVERY user in the department
                foreach (var u in deptUsers)
                {
                    var uNode = nodeMap[u.Id];
                    uNode.Subordinates = deptUsers.Where(sub => sub.ManagerId == u.Id).Select(sub => nodeMap[sub.Id]).ToList();
                    uNode.HasUnloadedChildren = uNode.Subordinates.Count > 0;
                }

                // Then, only add top-level users (whose manager is NOT in this department) to the department node
                var topLevelUsers = deptUsers.Where(u => u.ManagerId == null || !deptUsers.Any(du => du.Id == u.ManagerId)).ToList();
                foreach (var topUser in topLevelUsers)
                {
                    deptNode.Subordinates.Add(nodeMap[topUser.Id]);
                }
                
                deptNode.HasUnloadedChildren = deptNode.Subordinates.Count > 0;
                return deptNode;
            }

            OrgChartNode CreateSiteNode(string siteName, IEnumerable<User> siteUsers)
            {
                var siteNode = new OrgChartNode { UserId = Guid.NewGuid(), Name = siteName, Role = "Site", Initials = siteName.Length >= 2 ? siteName.Substring(0, 2).ToUpperInvariant() : "ST", IsExpanded = false, Subordinates = new List<OrgChartNode>(), HasUnloadedChildren = false };
                var siteAdmins = siteUsers.Where(u => u.Role == UserRole.Admin).ToList();
                
                foreach (var admin in siteAdmins)
                {
                    var adminNode = nodeMap[admin.Id];
                    adminNode.Subordinates = new List<OrgChartNode>();
                    var adminDepts = siteUsers.Where(u => u.Department != null && u.Department.AdminId == admin.Id).Select(u => u.Department!).DistinctBy(d => d.Id);
                    foreach (var dept in adminDepts)
                    {
                        adminNode.Subordinates.Add(CreateDepartmentNode(dept, siteUsers.Where(u => u.DepartmentId == dept.Id)));
                    }
                    adminNode.HasUnloadedChildren = adminNode.Subordinates.Count > 0;
                    siteNode.Subordinates.Add(adminNode);
                }

                // Handle users/departments without an admin
                var unassignedDepts = siteUsers.Where(u => u.Department != null && u.Department.AdminId == null).Select(u => u.Department!).DistinctBy(d => d.Id);
                foreach (var dept in unassignedDepts)
                {
                    siteNode.Subordinates.Add(CreateDepartmentNode(dept, siteUsers.Where(u => u.DepartmentId == dept.Id)));
                }

                siteNode.HasUnloadedChildren = siteNode.Subordinates.Count > 0;
                return siteNode;
            }

            OrgChartNode CreateCityNode(string cityName, IEnumerable<User> cityUsers)
            {
                var cityNode = new OrgChartNode { UserId = Guid.NewGuid(), Name = cityName, Role = "City", Initials = cityName.Length >= 2 ? cityName.Substring(0, 2).ToUpperInvariant() : "CT", IsExpanded = false, Subordinates = new List<OrgChartNode>(), HasUnloadedChildren = false };
                foreach (var siteGroup in cityUsers.GroupBy(u => u.Site).OrderBy(g => g.Key))
                {
                    cityNode.Subordinates.Add(CreateSiteNode(siteGroup.Key, siteGroup));
                }
                cityNode.HasUnloadedChildren = cityNode.Subordinates.Count > 0;
                return cityNode;
            }

            var countryManager = activeUsers.FirstOrDefault(u => u.Role == UserRole.CountryManager);
            
            OrgChartNode root;
            if (countryManager != null)
            {
                var initials = string.Concat(countryManager.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[0].ToString())).ToUpperInvariant();
                if (initials.Length > 2) initials = initials.Substring(0, 2);

                root = new OrgChartNode
                {
                    UserId = countryManager.Id,
                    Name = countryManager.Name,
                    Role = "Country Manager",
                    Department = requestingUser.Region?.Name ?? "Company",
                    Initials = initials,
                    IsExpanded = true,
                    Subordinates = new List<OrgChartNode>(),
                    HasUnloadedChildren = false
                };
            }
            else
            {
                root = new OrgChartNode
                {
                    UserId = Guid.Empty,
                    Name = requestingUser.Region?.Name ?? "Company",
                    Role = "Headquarters",
                    Department = "All Departments",
                    Initials = "HQ",
                    IsExpanded = true,
                    Subordinates = new List<OrgChartNode>(),
                    HasUnloadedChildren = false
                };
            }

            var usersToGroup = activeUsers.Where(u => u.Id != root.UserId).ToList();

            foreach (var cityGroup in usersToGroup.GroupBy(u => u.City ?? "Unknown").OrderBy(g => g.Key))
            {
                root.Subordinates.Add(CreateCityNode(cityGroup.Key, cityGroup));
            }
            root.HasUnloadedChildren = root.Subordinates.Count > 0;

            // Expand path to current user
            bool ExpandPathToUser(OrgChartNode node, Guid targetId)
            {
                if (node.UserId == targetId)
                {
                    node.IsExpanded = true;
                    return true;
                }
                bool foundInSubtree = false;
                foreach (var child in node.Subordinates)
                {
                    if (ExpandPathToUser(child, targetId))
                    {
                        foundInSubtree = true;
                    }
                }
                if (foundInSubtree)
                {
                    node.IsExpanded = true;
                }
                return foundInSubtree;
            }

            // Base expansion logic
            if (isAdmin)
            {
                // Admins see down to departments by default
                SetAllExpanded(root, false);
                root.IsExpanded = true;
                foreach (var city in root.Subordinates)
                {
                    city.IsExpanded = true;
                    foreach (var site in city.Subordinates)
                    {
                        site.IsExpanded = true;
                        foreach (var admin in site.Subordinates)
                        {
                            if (admin.UserId == currentUserId || requestingUser.Role == UserRole.CountryManager)
                            {
                                admin.IsExpanded = true; // Open their own departments
                            }
                        }
                    }
                }
            }

            // Always ensure the current user's path is expanded so they are visible
            ExpandPathToUser(root, currentUserId);

            // Math Layout Passes
            CalculateSubtreeWidths(root);
            CalculateNodeCoordinates(root, 0, root.SubtreeWidth / 2, 0, 50.0);

            return root;
        }

        private static void SetAllExpanded(OrgChartNode node, bool expanded)
        {
            node.IsExpanded = expanded;
            foreach (var child in node.Subordinates)
            {
                SetAllExpanded(child, expanded);
            }
        }

        public static void CalculateSubtreeWidths(OrgChartNode node)
        {
            if (!node.IsExpanded || node.Subordinates.Count == 0)
            {
                node.SubtreeWidth = 80.0; // Base width for a single node / collapsed branch
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

        public static void CalculateNodeCoordinates(OrgChartNode node, int depth, double x, int siblingIndex, double currentY)
        {
            node.Depth = depth;
            node.X = x;
            
            // Stagger siblings to save space (odd indices are 40px lower)
            // The stagger is added to the current accumulated Y, so the whole subtree shifts down.
            double stagger = (siblingIndex % 2 == 1) ? 40.0 : 0.0;
            node.Y = currentY + stagger;

            if (node.IsExpanded && node.Subordinates.Count > 0)
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

        public async Task<Contract?> GetActiveContractForUserAsync(Guid userId)
        {
            return await _contractGateway.GetActiveContractByUserIdAsync(userId);
        }

        public async Task SaveUserContractAsync(
            Guid adminId,
            Guid userId,
            ContractType type,
            ContractStatus status,
            DateOnly startDate,
            DateOnly? endDate,
            string? notes)
        {
            await EnsureRegionalAdminCanManageAsync(adminId, userId);

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

        private async Task<User> EnsureRegionalAdminCanManageAsync(Guid adminId, Guid userId)
        {
            var admin = await _userGateway.GetUserByIdAsync(adminId);
            if (admin == null)
                throw new EntityNotFoundException($"No administrator with id {adminId}.");
            if (admin.Role != UserRole.Admin)
                throw new UnauthorizedException("Only administrators can manage employee accounts.");

            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");
            if (user.RegionId != admin.RegionId)
                throw new UnauthorizedException("You can preview other regions, but you cannot edit their employees.");

            // If it's a Site Admin, ensure they have rights over the user's department,
            // or the user is the admin themselves.
            if (admin.Role == UserRole.Admin && user.Id != admin.Id)
            {
                if (user.Department == null || user.Department.AdminId != admin.Id)
                {
                    throw new UnauthorizedException("You can only edit employees in departments assigned to your site administration.");
                }
            }

            return user;
        }
    }
}
