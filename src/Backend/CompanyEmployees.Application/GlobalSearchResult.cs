namespace CompanyEmployees.Application
{
    // Which kind of thing the caller wants back. All is the default and the only value that
    // fills every list; the rest fill one and leave the others empty — but the totals are
    // computed regardless, because the chips that switch between them show those counts and
    // would otherwise read zero for everything the user is not currently looking at.
    public enum SearchEntityType
    {
        All = 0,
        People = 1,
        Departments = 2,
        Regions = 3
    }

    public sealed record PersonHit(
        Guid Id,
        string Name,
        string Email,
        string Role,
        string Department,
        string Region,
        string Initials);

    // MemberCount is how many people the *caller* can see in it, not the true headcount:
    // a count that includes rows the search will never return sends people down dead ends,
    // which is the one thing facet counts exist to prevent.
    public sealed record DepartmentHit(Guid Id, string Name, string ManagerName, int MemberCount);

    public sealed record RegionHit(Guid Id, string Name, string Code, int MemberCount);

    public sealed class GlobalSearchResult
    {
        public List<PersonHit> People { get; set; } = [];
        public List<DepartmentHit> Departments { get; set; } = [];
        public List<RegionHit> Regions { get; set; } = [];

        // Totals before the per-type cap, so a chip can say "People 34" while the dropdown
        // shows the first eight.
        public int PeopleTotal { get; set; }
        public int DepartmentsTotal { get; set; }
        public int RegionsTotal { get; set; }

        public int Total => PeopleTotal + DepartmentsTotal + RegionsTotal;
    }
}
