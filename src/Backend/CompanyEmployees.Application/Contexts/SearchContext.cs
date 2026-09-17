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

        // Performs a cross-cutting search across people, departments, and regions.
        // An empty query with filters returns browsable entities within that scope.
        public async Task<GlobalSearchResult> GlobalSearchAsync(
            Guid userId,
            string? query,
            Guid? regionId = null,
            Guid? departmentId = null,
            SearchEntityType type = SearchEntityType.All,
            int take = 8)
        {
            // Ensure caller exists before executing search.
            _ = await _userGateway.GetUserByIdAsync(userId)
                ?? throw new EntityNotFoundException($"No user with id {userId}.");

            // Directory search is worldwide; actions remain region-scoped.
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

            // Group by foreign key to avoid reference grouping under AsNoTracking.
            // Manager names are fetched via IDepartmentGateway since user navigation does not include them.
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

            // Totals are computed across all matching entities before pagination or type filtering.
            var result = new GlobalSearchResult
            {
                PeopleTotal = people.Count,
                DepartmentsTotal = departments.Count,
                RegionsTotal = regions.Count
            };

            // Default browsing state shows departments and regions rather than an arbitrary list of people.
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
