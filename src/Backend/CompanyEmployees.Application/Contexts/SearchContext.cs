using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Application.Contexts
{
    public class SearchContext : BaseContext
    {
        private readonly IUserGateway _userGateway;
        private readonly IDepartmentGateway _departmentGateway;

        public SearchContext(
            ILogger<SearchContext> logger,
            IUserGateway userGateway,
            IDepartmentGateway departmentGateway) : base(logger)
        {
            _userGateway = userGateway;
            _departmentGateway = departmentGateway;
        }

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
            user.City,
            user.Site,
            InitialsOf(user.Name));

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
