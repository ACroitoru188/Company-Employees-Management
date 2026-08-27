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
        private readonly ImpersonationContext _impersonation;
        private readonly IDelegatedActionGateway _delegatedActions;

        public EmployeeContext(
            ILogger<EmployeeContext> logger,
            ILeaveRequestGateway leaveRequestGateway,
            IUserGateway userGateway,
            IDepartmentGateway departmentGateway,
            IRegionGateway regionGateway,
            IPublicHolidayProvider holidayProvider,
            IContractGateway contractGateway,
            IManagerDelegationGateway delegationGateway,
            NotificationContext notifications,
            ImpersonationContext impersonation,
            IDelegatedActionGateway delegatedActions) : base(logger)
        {
            _leaveRequestGateway = leaveRequestGateway;
            _userGateway = userGateway;
            _departmentGateway = departmentGateway;
            _regionGateway = regionGateway;
            _holidayProvider = holidayProvider;
            _contractGateway = contractGateway;
            _delegationGateway = delegationGateway;
            _notifications = notifications;
            _impersonation = impersonation;
            _delegatedActions = delegatedActions;
        }

        // The same shape as ManagerContext's guard, and deliberately not shared with it: both
        // defer to ImpersonationContext, which is where the single implementation of "is this
        // delegation still good" lives. Null means the caller is acting as themselves.
        private async Task<ManagerDelegation?> GuardAsync(Guid actingAsUserId, ActingOnBehalf? onBehalf)
        {
            if (onBehalf is null)
                return null;

            return await _impersonation.ValidateDelegationAsync(
                onBehalf.RealUserId, onBehalf.DelegationId, actingAsUserId);
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

        public async Task<User> GetEmployeeByEmailAsync(string email)
        {
            var user = await _userGateway.GetUserByEmailAsync(email);
            if (user == null)
                throw new EntityNotFoundException($"No user with email {email}.");
            return user;
        }

        public async Task<User> GetEmployeeByIdAsync(Guid userId)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");
            return user;
        }

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
            var holidays = (await _holidayProvider.GetHolidaysAsync(user.Region.Code, year))
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

            var previousYearHolidays = (await _holidayProvider
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
                var twoYearsAgoHolidays = (await _holidayProvider
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

            return await _holidayProvider.GetHolidaysAsync(user.Region.Code, year);
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



        // The whole company, lazily. Everyone may see everyone (2026-08-17), which is several
        // hundred accounts — so exactly two things are open when the chart loads: the chain of
        // managers above the viewer, and the viewer's own reports. Every other branch arrives
        // through GetOrgChartChildrenAsync when somebody expands it.
        //
        // This replaced a builder that assembled the tree around the *viewer* — a non-admin got
        // their own team branch and nothing else, an admin got one level of synthetic
        // department-group nodes that could never be expanded because the loader was never
        // wired up. Neither could show the company.
        public async Task<OrgChartNode?> GetCompanyOrgChartAsync(Guid currentUserId)
        {
            var activeUsers = (await _userGateway.GetAllUsersAsync())
                .Where(user => user.Status == UserStatus.Active)
                .ToList();

            var viewer = activeUsers.FirstOrDefault(user => user.Id == currentUserId)
                ?? throw new EntityNotFoundException($"No user with id {currentUserId}.");

            var byId = activeUsers.ToDictionary(user => user.Id);

            // A manager who is inactive is not in byId, so their reports are treated as tops of
            // the chart rather than vanishing under a parent that is never drawn.
            var childrenOf = activeUsers
                .Where(user => user.ManagerId is Guid managerId && byId.ContainsKey(managerId))
                .GroupBy(user => user.ManagerId!.Value)
                .ToDictionary(group => group.Key, group => group.OrderBy(user => user.Name).ToList());

            var pending = await _leaveRequestGateway.GetAllCompanyPendingRequestsAsync();

            var nodes = new Dictionary<Guid, OrgChartNode>();
            OrgChartNode NodeFor(User user)
            {
                if (!nodes.TryGetValue(user.Id, out var node))
                {
                    nodes[user.Id] = node = BuildOrgChartNode(user, pending);
                    node.HasUnloadedChildren = childrenOf.ContainsKey(user.Id);
                    node.IsExpanded = false;
                }

                return node;
            }

            // Every region has its own top, so there is no single chief executive to root the
            // chart on — hence the heading. Its empty id is what the page's action checks use to
            // recognise a node nobody can act on.
            var root = new OrgChartNode
            {
                UserId = Guid.Empty,
                Name = "Company",
                Role = "Headquarters",
                Department = "All Departments",
                Initials = "HQ",
                IsExpanded = true,
                IsSyntheticGroup = true
            };

            // Root-level users — no manager in the visible set — grouped by region first: the
            // chart is worldwide, so without this every region's tops landed in one flat
            // alphabetical list with nothing saying which country they belonged to. Grouped by
            // RegionId, not by the Region navigation instance: GetAllUsersAsync reads
            // AsNoTracking without identity resolution, so every user carries its *own* Region
            // object, and grouping on those groups by reference — the same trap GlobalSearchAsync
            // hit before it was fixed the same way.
            var topLevel = activeUsers
                .Where(user => user.ManagerId is not Guid managerId || !byId.ContainsKey(managerId))
                .ToList();

            root.Subordinates = topLevel
                .Where(user => user.Region is not null)
                .GroupBy(user => user.RegionId)
                .OrderBy(group => group.First().Region!.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new OrgChartNode
                {
                    UserId = Guid.NewGuid(),
                    Name = group.First().Region!.Name,
                    Role = "Region",
                    Initials = InitialsOf(group.First().Region!.Name),
                    IsExpanded = false,
                    IsSyntheticGroup = true,
                    Subordinates = GroupByLocation(group, NodeFor)
                })
                .ToList();

            // Upwards from the viewer to a top. Add returns false on a repeat, which is also the
            // guard against a cycle in the reporting data.
            var chain = new List<User>();
            var seen = new HashSet<Guid>();
            var current = viewer;
            while (current != null && seen.Add(current.Id))
            {
                chain.Add(current);
                current = current.ManagerId is Guid managerId && byId.TryGetValue(managerId, out var manager)
                    ? manager
                    : null;
            }

            // Opening the path attaches each step's whole team, not just the next step, so the
            // chart reads as an organisation on arrival instead of a single line of boxes.
            foreach (var person in chain)
            {
                if (!childrenOf.TryGetValue(person.Id, out var reports))
                    continue;

                var node = NodeFor(person);
                node.Subordinates = reports.Select(NodeFor).ToList();
                node.IsExpanded = true;
                node.HasUnloadedChildren = false;
            }

            // The chain above only opens the reporting graph; the viewer's top-of-chain is now
            // behind Region and, maybe, City/Site nodes that the chain knows nothing about. Walk
            // down from the root to find and open whatever sits above the chain's own top.
            ExpandPathToNode(root, chain[^1].Id);

            NodeFor(viewer).IsFocusNode = true;
            return root;
        }

        // A region's root-level people, grouped by City then Site where they have them set.
        // Nobody with neither attaches a level below where they always did: directly under the
        // region. Small, so built eagerly rather than lazily — a region with a hundred
        // top-level accounts and no manager between them would be unusual.
        private static List<OrgChartNode> GroupByLocation(
            IEnumerable<User> people, Func<User, OrgChartNode> nodeFor)
        {
            var byCity = people
                .GroupBy(user => string.IsNullOrWhiteSpace(user.City) ? null : user.City)
                .OrderBy(group => group.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            var result = new List<OrgChartNode>();
            foreach (var cityGroup in byCity)
            {
                if (cityGroup.Key is not string city)
                {
                    // No city on record: skip straight to the person, same as before this
                    // hierarchy existed.
                    result.AddRange(cityGroup.OrderBy(user => user.Name).Select(nodeFor));
                    continue;
                }

                var bySite = cityGroup
                    .GroupBy(user => string.IsNullOrWhiteSpace(user.Site) ? null : user.Site)
                    .OrderBy(group => group.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase);

                var cityChildren = new List<OrgChartNode>();
                foreach (var siteGroup in bySite)
                {
                    if (siteGroup.Key is not string site)
                    {
                        cityChildren.AddRange(siteGroup.OrderBy(user => user.Name).Select(nodeFor));
                        continue;
                    }

                    cityChildren.Add(new OrgChartNode
                    {
                        UserId = Guid.NewGuid(),
                        Name = site,
                        Role = "Site",
                        Initials = InitialsOf(site),
                        IsExpanded = false,
                        IsSyntheticGroup = true,
                        Subordinates = siteGroup.OrderBy(user => user.Name).Select(nodeFor).ToList()
                    });
                }

                result.Add(new OrgChartNode
                {
                    UserId = Guid.NewGuid(),
                    Name = city,
                    Role = "City",
                    Initials = InitialsOf(city),
                    IsExpanded = false,
                    IsSyntheticGroup = true,
                    Subordinates = cityChildren
                });
            }

            return result;
        }

        // Walks down from a node looking for targetId, opening every synthetic group on the way
        // (a real person is opened by the chain-walking caller instead, which also has to fill
        // in their team). Shared by the two callers that root a tree away from the person they
        // need visible: the company chart's own viewer, and the focused tree's top-of-chain.
        private static bool ExpandPathToNode(OrgChartNode node, Guid targetId)
        {
            if (node.UserId == targetId)
                return true;

            foreach (var child in node.Subordinates)
            {
                if (!ExpandPathToNode(child, targetId))
                    continue;

                if (child.IsSyntheticGroup)
                    child.IsExpanded = true;
                return true;
            }

            return false;
        }

        // One level of the tree, fetched when a node is expanded. Without this the worldwide
        // chart would have to materialise every account up front; with it, a branch costs one
        // query at the moment somebody asks for it.
        public async Task<List<OrgChartNode>> GetOrgChartChildrenAsync(Guid parentUserId)
        {
            var activeUsers = (await _userGateway.GetAllUsersAsync())
                .Where(user => user.Status == UserStatus.Active)
                .ToList();

            var managersWithReports = activeUsers
                .Where(user => user.ManagerId is not null)
                .Select(user => user.ManagerId!.Value)
                .ToHashSet();

            var pending = await _leaveRequestGateway.GetAllCompanyPendingRequestsAsync();

            return activeUsers
                .Where(user => user.ManagerId == parentUserId)
                .OrderBy(user => user.Name)
                .Select(user =>
                {
                    var node = BuildOrgChartNode(user, pending);
                    node.HasUnloadedChildren = managersWithReports.Contains(user.Id);
                    node.IsExpanded = false;
                    return node;
                })
                .ToList();
        }


        // One search behind the whole app: the header box, and the /search page it opens.
        //
        // regionId and departmentId are the scope pills, not text the user typed — picking a
        // region or a department out of the results narrows everything that follows, which is
        // how "Romania, then Design, then the person" is answered without anyone learning a
        // query syntax. An empty query with a pill set is a legitimate search: it means
        // "show me what is in here".
        //
        // In memory over GetAllUsersAsync, like the org chart and every other cross-cutting
        // read in this class. At a hundred accounts that is a non-issue; if the roster ever
        // grows past a few thousand this is the first thing to push into the gateway.
        public async Task<GlobalSearchResult> GlobalSearchAsync(
            Guid userId,
            string? query,
            Guid? regionId = null,
            Guid? departmentId = null,
            SearchEntityType type = SearchEntityType.All,
            int take = 8)
        {
            // Resolved even though the result is no longer filtered by it: an unknown caller
            // must still be refused rather than served the whole company.
            _ = await _userGateway.GetUserByIdAsync(userId)
                ?? throw new EntityNotFoundException($"No user with id {userId}.");

            // The company directory is deliberately worldwide (2026-08-17): everyone may look
            // up anyone, in any region, exactly as they can in the org chart. What stays
            // region-scoped is *doing* things — decisions, contracts, team rosters, dashboards
            // and every CSV export. Widening this without keeping those scoped is the mistake
            // to avoid; see "Who can see whom" in CLAUDE.md.
            var visible = (await _userGateway.GetAllUsersAsync())
                .Where(user => user.Status == UserStatus.Active)
                .ToList();

            if (regionId is Guid region)
                visible = visible.Where(user => user.RegionId == region).ToList();
            if (departmentId is Guid department)
                visible = visible.Where(user => user.DepartmentId == department).ToList();

            var terms = (query ?? string.Empty).Trim();
            var hasQuery = terms.Length > 0;

            var people = visible
                .Where(user => !hasQuery || MatchesPerson(user, terms))
                .OrderBy(user => user.Name)
                .ToList();

            // Grouped by the foreign key, never by the navigation instance. GetAllUsersAsync
            // reads AsNoTracking without identity resolution, so every user carries its *own*
            // Department and Region objects — grouping by those groups by reference and yields
            // one "department" per employee, each with a member count of 1.
            // The manager's name comes from the department gateway, not from the users: the user
            // query includes Department but not Department.Manager, so reading it off a user's
            // navigation gives null every time and the column renders permanently empty.
            var departmentsById = (await _departmentGateway.GetAllAsync())
                .ToDictionary(department => department.Id);

            var departments = visible
                .Where(user => user.DepartmentId is not null && user.Department is not null)
                .GroupBy(user => user.DepartmentId!.Value)
                .Select(group => new DepartmentHit(
                    group.Key,
                    group.First().Department!.Name,
                    departmentsById.TryGetValue(group.Key, out var department)
                        ? department.Manager?.Name ?? string.Empty
                        : string.Empty,
                    group.Count()))
                .Where(hit => !hasQuery || Contains(hit.Name, terms) || Contains(hit.ManagerName, terms))
                .OrderBy(hit => hit.Name)
                .ToList();

            var regions = visible
                .Where(user => user.Region is not null)
                .GroupBy(user => user.RegionId)
                .Select(group => new RegionHit(
                    group.Key,
                    group.First().Region!.Name,
                    group.First().Region!.Code,
                    group.Count()))
                .Where(hit => !hasQuery || Contains(hit.Name, terms) || Contains(hit.Code, terms))
                .OrderBy(hit => hit.Name)
                .ToList();

            // Counted before the cap and before the type filter: the chips are how the user
            // switches type, so each has to report what is waiting behind it — including the
            // people chip in the opening state, which is what makes it worth pressing.
            var result = new GlobalSearchResult
            {
                PeopleTotal = people.Count,
                DepartmentsTotal = departments.Count,
                RegionsTotal = regions.Count
            };

            // Nothing typed and nothing pinned is the dropdown's opening state: the places to
            // drill into are more use there than the first few names in the company in
            // alphabetical order. Asking for People explicitly overrides that — then listing
            // everyone is exactly what was asked for.
            var browsing = !hasQuery
                           && regionId is null
                           && departmentId is null
                           && type != SearchEntityType.People;

            if (!browsing && type is SearchEntityType.All or SearchEntityType.People)
                result.People = people.Take(take).Select(ToPersonHit).ToList();
            if (type is SearchEntityType.All or SearchEntityType.Departments)
                result.Departments = departments.Take(take).ToList();
            if (type is SearchEntityType.All or SearchEntityType.Regions)
                result.Regions = regions.Take(take).ToList();

            return result;
        }

        private static bool MatchesPerson(User user, string terms) =>
            Contains(user.Name, terms)
            || Contains(user.Email, terms)
            || Contains(user.Role.ToString(), terms)
            || Contains(user.Department?.Name, terms)
            || Contains(user.Region?.Name, terms);

        private static bool Contains(string? value, string terms) =>
            !string.IsNullOrWhiteSpace(value)
            && value.Contains(terms, StringComparison.OrdinalIgnoreCase);

        private static PersonHit ToPersonHit(User user) => new(
            user.Id,
            user.Name,
            user.Email ?? string.Empty,
            user.Role.ToString(),
            user.Department?.Name ?? string.Empty,
            user.Region?.Name ?? string.Empty,
            InitialsOf(user.Name));

        private static string InitialsOf(string name)
        {
            var initials = string.Concat(name
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part[0]))
                .ToUpperInvariant();

            return initials.Length > 2 ? initials[..2] : initials;
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
                foreach (var holiday in await _holidayProvider.GetHolidaysAsync(
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


        // The org chart the global search lands on. GetCompanyOrgChartAsync deliberately builds
        // a narrow tree — a non-admin gets their own team branch, an admin gets one unexpanded
        // level of department groups — so the person just searched for is almost never in it,
        // and asking the page to expand a path to them could only ever fail.
        //
        // This builds the tree *around* the target instead: their whole management chain, the
        // colleagues they share a manager with, and their own direct reports. Worldwide, like
        // the search that produced the link. Returns null for an unknown or inactive target.
        public async Task<OrgChartNode?> GetOrgChartFocusedOnAsync(Guid currentUserId, Guid targetUserId)
        {
            var allUsers = await _userGateway.GetAllUsersAsync();

            // Checked, not filtered by: an unknown caller is refused, but a known one may look
            // at anybody — the directory is worldwide.
            _ = allUsers.FirstOrDefault(user => user.Id == currentUserId)
                ?? throw new EntityNotFoundException($"No user with id {currentUserId}.");

            var visible = allUsers
                .Where(user => user.Status == UserStatus.Active)
                .ToList();

            // Only an inactive or non-existent id lands here now.
            var target = visible.FirstOrDefault(user => user.Id == targetUserId);
            if (target == null)
                return null;

            var pending = await _leaveRequestGateway.GetAllCompanyPendingRequestsAsync();
            var nodes = new Dictionary<Guid, OrgChartNode>();
            OrgChartNode NodeFor(User user)
            {
                if (!nodes.TryGetValue(user.Id, out var node))
                    nodes[user.Id] = node = BuildOrgChartNode(user, pending);
                return node;
            }

            // Upwards from the target, stopping at the first manager outside the visible set —
            // a cross-region manager is not something to reveal here. Guarded against a cycle
            // for the same reason GetCompanyOrgChartAsync guards: bad data must not hang a page.
            var chain = new List<User>();
            var seen = new HashSet<Guid>();
            var current = target;
            while (current != null && seen.Add(current.Id))
            {
                chain.Add(current);
                current = current.ManagerId is Guid managerId
                    ? visible.FirstOrDefault(user => user.Id == managerId)
                    : null;
            }
            chain.Reverse();

            // Everything already on the path from the root down. Nothing below may attach one
            // of these again: a cycle in the reporting data would otherwise produce a cyclic
            // *node* graph, and the first recursive walk over it — expanding, rendering —
            // never returns. The chain walk above stops at a repeat; this stops the branches.
            var placed = chain.Select(user => user.Id).ToHashSet();

            for (var i = 0; i < chain.Count - 1; i++)
                NodeFor(chain[i]).Subordinates = new List<OrgChartNode> { NodeFor(chain[i + 1]) };

            // The target's own team: everyone reporting to the same manager, so the person is
            // shown among their colleagues rather than as a lone node on a stick.
            if (target.ManagerId is Guid targetManagerId
                && visible.FirstOrDefault(user => user.Id == targetManagerId) is { } targetManager)
            {
                var team = visible
                    .Where(user => user.ManagerId == targetManagerId
                                   && (user.Id == target.Id || placed.Add(user.Id)))
                    .OrderBy(user => user.Name)
                    .ToList();

                NodeFor(targetManager).Subordinates = team.Select(NodeFor).ToList();
            }

            NodeFor(target).Subordinates = visible
                .Where(user => user.ManagerId == target.Id && placed.Add(user.Id))
                .OrderBy(user => user.Name)
                .Select(NodeFor)
                .ToList();

            var root = NodeFor(chain[0]);
            SetAllExpanded(root, true);

            // Wraps the top of the chain in the same Region/City/Site path the worldwide chart
            // would put them behind, innermost first, so a focused tree reads as a branch of
            // that one rather than a fragment nobody can place. Skipped levels the person has
            // no value for — most chains stop at Region, since City/Site are optional.
            var topOfChain = chain[0];
            var wrappers = new List<(string Name, string Role)>();
            if (topOfChain.Region is not null)
                wrappers.Add((topOfChain.Region.Name, "Region"));
            if (!string.IsNullOrWhiteSpace(topOfChain.City))
                wrappers.Add((topOfChain.City!, "City"));
            if (!string.IsNullOrWhiteSpace(topOfChain.Site))
                wrappers.Add((topOfChain.Site!, "Site"));

            for (var i = wrappers.Count - 1; i >= 0; i--)
            {
                root = new OrgChartNode
                {
                    UserId = Guid.NewGuid(),
                    Name = wrappers[i].Name,
                    Role = wrappers[i].Role,
                    Initials = InitialsOf(wrappers[i].Name),
                    IsExpanded = true,
                    IsSyntheticGroup = true,
                    Subordinates = new List<OrgChartNode> { root }
                };
            }

            // The one node the page should highlight and scroll to.
            foreach (var node in nodes.Values)
                node.IsFocusNode = node.UserId == targetUserId;

            return root;
        }

        // Everyone below a manager, however deep. Answers "may I act on this row?" for the org
        // chart, which since the directory went worldwide shows plenty of people the viewer may
        // only look at.
        //
        // Computed from the reporting graph rather than from the rendered tree: the focused view
        // is built around somebody else and often does not contain the viewer at all, so walking
        // it found no subtree and quietly took a manager's own buttons away.
        //
        // Region-scoped, because acting is: ManagerContext refuses a decision or a contract
        // across regions, and a relocation drops reporting links that would cross one.
        public async Task<HashSet<Guid>> GetManagedUserIdsAsync(Guid managerId)
        {
            var manager = await _userGateway.GetUserByIdAsync(managerId);
            if (manager == null)
                return [];

            var byManager = (await _userGateway.GetAllUsersAsync())
                .Where(user => user.Status == UserStatus.Active
                               && user.RegionId == manager.RegionId
                               && user.ManagerId != null)
                .GroupBy(user => user.ManagerId!.Value)
                .ToDictionary(group => group.Key, group => group.Select(user => user.Id).ToList());

            var managed = new HashSet<Guid>();
            var queue = new Queue<Guid>();
            queue.Enqueue(managerId);

            while (queue.Count > 0)
            {
                if (!byManager.TryGetValue(queue.Dequeue(), out var reports))
                    continue;

                // Add returns false on a repeat, which is also the cycle guard.
                foreach (var report in reports.Where(managed.Add))
                    queue.Enqueue(report);
            }

            return managed;
        }

        private OrgChartNode BuildOrgChartNode(User user, List<LeaveRequest> pendingRequests)
        {
            var initials = string.Concat(user.Name
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part[0].ToString()))
                .ToUpperInvariant();
            if (initials.Length > 2)
                initials = initials[..2];

            var activeContract = user.Contracts?.FirstOrDefault(c => c.Status == ContractStatus.Active);
            var pending = pendingRequests.FirstOrDefault(request => request.UserId == user.Id);

            return new OrgChartNode
            {
                UserId = user.Id,
                Name = user.Name,
                Email = user.Email ?? string.Empty,
                Role = user.Role.ToString(),
                Department = user.Department?.Name ?? string.Empty,
                Initials = initials,
                ManagerId = user.ManagerId,
                RegionId = user.RegionId,
                Region = user.Region?.Name ?? string.Empty,
                City = user.City,
                Site = user.Site,
                HasPendingRequest = pending != null,
                PendingRequestId = pending?.Id,
                PendingRequestType = pending?.Type.ToString(),
                PendingRequestDates = pending != null
                    ? $"{pending.StartDate:MMM d} – {pending.EndDate:MMM d, yyyy}"
                    : null,
                HasContract = activeContract != null,
                ContractId = activeContract?.Id,
                ContractType = activeContract?.Type,
                ContractStatus = activeContract?.Status,
                ContractStartDate = activeContract?.StartDate,
                ContractEndDate = activeContract?.EndDate
            };
        }

        private static void SetAllExpanded(OrgChartNode node, bool expanded)
        {
            node.IsExpanded = expanded;
            foreach (var child in node.Subordinates)
            {
                SetAllExpanded(child, expanded);
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

            return user;
        }
    }
}
