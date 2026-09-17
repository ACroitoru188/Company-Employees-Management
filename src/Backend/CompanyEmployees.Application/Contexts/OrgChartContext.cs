using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Application.Contexts
{
    public class OrgChartContext : BaseContext
    {
        private readonly IUserGateway _userGateway;
        private readonly ILeaveRequestGateway _leaveRequestGateway;

        public OrgChartContext(
            ILogger<OrgChartContext> logger,
            IUserGateway userGateway,
            ILeaveRequestGateway leaveRequestGateway) : base(logger)
        {
            _userGateway = userGateway;
            _leaveRequestGateway = leaveRequestGateway;
        }

        // This replaced a builder that assembled the tree around the *viewer* — a non-admin got
        // their own team branch and nothing else, an admin got one level of synthetic
        // department-group nodes that could never be expanded because the loader was never
        // wired up. Neither could show the company.
        public async Task<OrgChartNode?> GetCompanyOrgChartAsync(
            Guid currentUserId,
            Guid? targetUserId = null,
            string? cityFilter = null,
            string? siteFilter = null,
            string? deptFilter = null)
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

            HashSet<Guid> GetPathToRoot(User u)
            {
                var path = new HashSet<Guid>();
                var curr = u;
                while (curr != null)
                {
                    path.Add(curr.Id);
                    if (curr.ManagerId.HasValue && byId.TryGetValue(curr.ManagerId.Value, out var mgr) && !path.Contains(curr.ManagerId.Value))
                        curr = mgr;
                    else
                        break;
                }
                return path;
            }

            var viewerPath = GetPathToRoot(viewer);
            var targetPath = targetUserId.HasValue && byId.TryGetValue(targetUserId.Value, out var tgt) ? GetPathToRoot(tgt) : new HashSet<Guid>();

            // Filter out peers from the entire upward chain to keep the org chart clean
            foreach (var managerId in viewerPath.Union(targetPath))
            {
                if (managerId == viewer.Id) continue;
                if (targetUserId.HasValue && managerId == targetUserId.Value) continue;

                if (childrenOf.ContainsKey(managerId))
                {
                    // An employee is allowed to see their own immediate peers
                    if (managerId == viewer.ManagerId && viewer.Role == UserRole.Employee)
                        continue;

                    childrenOf[managerId] = childrenOf[managerId]
                        .Where(u => viewerPath.Contains(u.Id) || targetPath.Contains(u.Id))
                        .ToList();
                }
            }

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

            Guid GetRootId(User u)
            {
                var curr = u;
                var vis = new HashSet<Guid>();
                while (curr.ManagerId.HasValue && byId.TryGetValue(curr.ManagerId.Value, out var mgr) && !vis.Contains(curr.ManagerId.Value))
                {
                    vis.Add(curr.ManagerId.Value);
                    curr = mgr;
                }
                return curr.Id;
            }

            var rootsToInclude = new HashSet<Guid> { GetRootId(viewer) };
            if (targetUserId.HasValue && byId.TryGetValue(targetUserId.Value, out var targetUserObj))
            {
                rootsToInclude.Add(GetRootId(targetUserObj));
            }

            // Top-level users are those without a valid manager in the system,
            // filtered to only show the hierarchies relevant to the viewer.
            var topLevelUsers = activeUsers
                .Where(user => (user.ManagerId == null || !byId.ContainsKey(user.ManagerId.Value)) && rootsToInclude.Contains(user.Id))
                .ToList();

            var visibleRegions = new HashSet<Guid>();
            if (viewer.RegionId != Guid.Empty)
                visibleRegions.Add(viewer.RegionId);
                
            if (targetUserId.HasValue && byId.TryGetValue(targetUserId.Value, out var t) && t.RegionId != Guid.Empty)
                visibleRegions.Add(t.RegionId);

            root.Subordinates = topLevelUsers
                .Where(user => user.Region is not null && visibleRegions.Contains(user.RegionId))
                .GroupBy(user => user.RegionId)
                .OrderBy(group => group.First().Region!.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => {
                    var regionNode = new OrgChartNode
                    {
                        UserId = Guid.NewGuid(),
                        Name = group.First().Region!.Name,
                        Role = "Region",
                        Initials = InitialsOf(group.First().Region!.Name),
                        IsExpanded = false,
                        IsSyntheticGroup = true,
                        Subordinates = group.Select(NodeFor).OrderBy(n => n.Name).ToList()
                    };
                    return regionNode;
                })
                .ToList();

            void ExpandChain(User startNode, bool markPath = false)
            {
                var chain = new List<User>();
                var seen = new HashSet<Guid>();
                var current = startNode;
                while (current != null && seen.Add(current.Id))
                {
                    chain.Add(current);
                    current = current.ManagerId is Guid managerId && byId.TryGetValue(managerId, out var manager)
                        ? manager
                        : null;
                }

                bool stopMarking = false;
                foreach (var person in chain)
                {
                    if (person.Id == currentUserId) stopMarking = true;
                    if (markPath && !stopMarking && person.Id != currentUserId)
                    {
                        NodeFor(person).IsFocusNode = true;
                    }

                    if (!childrenOf.TryGetValue(person.Id, out var reports))
                        continue;

                    var node = NodeFor(person);
                    node.Subordinates = reports.Select(NodeFor).ToList();
                    node.IsExpanded = true;
                    node.HasUnloadedChildren = false;
                }

                ExpandPathToNode(root, chain[^1].Id);
            }

            ExpandChain(viewer);

            if (targetUserId.HasValue && byId.TryGetValue(targetUserId.Value, out var target))
            {
                bool isOther = targetUserId.Value != currentUserId;
                ExpandChain(target, isOther);
                if (isOther)
                {
                    NodeFor(target).IsSearchResult = true;
                }
            }

            if (!string.IsNullOrEmpty(cityFilter) && cityFilter != "Toate" ||
                !string.IsNullOrEmpty(siteFilter) && siteFilter != "Toate" ||
                !string.IsNullOrEmpty(deptFilter) && deptFilter != "Toate")
            {
                var matchingUsers = activeUsers.Where(u =>
                    (string.IsNullOrEmpty(cityFilter) || cityFilter == "Toate" || u.City == cityFilter) &&
                    (string.IsNullOrEmpty(siteFilter) || siteFilter == "Toate" || u.Site == siteFilter) &&
                    (string.IsNullOrEmpty(deptFilter) || deptFilter == "Toate" || u.Department?.Name == deptFilter)
                ).ToList();

                foreach (var u in matchingUsers)
                {
                    ExpandChain(u);
                }
            }

            return root;
        }

        // Walks down from a node looking for targetId, opening every synthetic group on the way
        // (a real person is opened by the chain-walking caller instead, which also has to fill
        // in their team). Shared by the two callers that root a tree away from the person they
        // need visible: the company chart's own viewer, and the focused tree's top-of-chain.
        private static bool ExpandPathToNode(OrgChartNode node, Guid targetId)
        {
            if (node.UserId == targetId)
            {
                return true;
            }

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

            var parent = activeUsers.FirstOrDefault(u => u.Id == parentUserId);
            bool isTopLevel = parent != null && parent.ManagerId == null;

            var reports = activeUsers
                .Where(user => user.ManagerId == parentUserId)
                .OrderBy(user => user.Name)
                .ToList();

            OrgChartNode NodeFor(User user)
            {
                var node = BuildOrgChartNode(user, pending);
                node.HasUnloadedChildren = managersWithReports.Contains(user.Id);
                node.IsExpanded = false;
                return node;
            }

            return reports.Select(NodeFor).ToList();
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

        private static string InitialsOf(string name)
        {
            var initials = string.Concat(name
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part[0]))
                .ToUpperInvariant();

            return initials.Length > 2 ? initials[..2] : initials;
        }
    }
}
