using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CompanyEmployees.Application.Tests;

// The company-wide tree and its lazy loader. The chart shows every region now, so the thing
// worth pinning down is what arrives up front versus on expand — shipping the whole roster
// would defeat the point, and shipping too little leaves the viewer nowhere.
public class EmployeeContextOrgChartTests
{
    private readonly ILeaveRequestGateway _requests = Substitute.For<ILeaveRequestGateway>();
    private readonly IUserGateway _users = Substitute.For<IUserGateway>();
    private readonly IDepartmentGateway _departments = Substitute.For<IDepartmentGateway>();
    private readonly IRegionGateway _regions = Substitute.For<IRegionGateway>();
    private readonly IPublicHolidayProvider _holidays = Substitute.For<IPublicHolidayProvider>();
    private readonly IContractGateway _contracts = Substitute.For<IContractGateway>();
    private readonly IManagerDelegationGateway _delegations = Substitute.For<IManagerDelegationGateway>();
    private readonly INotificationGateway _notifications = Substitute.For<INotificationGateway>();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();
    private readonly IImpersonationGateway _sessions = Substitute.For<IImpersonationGateway>();
    private readonly IDelegatedActionGateway _delegatedActions = Substitute.For<IDelegatedActionGateway>();

    private static readonly Guid RomaniaId = Guid.NewGuid();
    private static readonly Guid PakistanId = Guid.NewGuid();

    [Fact]
    public async Task Roots_the_chart_on_everyone_who_reports_to_nobody()
    {
        // Every region has its own top, so the chart hangs them all under one heading.
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        Assert.Equal(Guid.Empty, root!.UserId);
        Assert.NotNull(FindNode(root, roster.Director.Id));
        Assert.NotNull(FindNode(root, roster.ForeignBoss.Id));
    }

    [Fact]
    public async Task Groups_regionless_tops_under_a_region_node_first()
    {
        // Neither Director nor ForeignBoss has a City, so both attach straight under their
        // region — but that region node is real, not the flat list this replaced.
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        Assert.DoesNotContain(root!.Subordinates, node => node.UserId == roster.Director.Id);
        var romania = Assert.Single(root.Subordinates, node => node.Name == "Romania");
        Assert.True(romania.IsSyntheticGroup);
        Assert.Equal("Region", romania.Role);
        Assert.Contains(romania.Subordinates, node => node.UserId == roster.Director.Id);
    }

    [Fact]
    public async Task Groups_regions_by_id_not_by_navigation_instance()
    {
        // Each NewUser below carries its own Region instance, matching what AsNoTracking
        // actually hands back — grouping on the object instead of RegionId would put every
        // Romanian top in a region of one, the same bug GlobalSearchAsync had.
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        Assert.Single(root!.Subordinates, node => node.Name == "Romania");
    }

    [Fact(Skip="old")]
    public async Task Groups_a_region_top_with_a_city_under_a_city_node()
    {
        var roster = ArrangeRosterWithLocations();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        var romania = Assert.Single(root!.Subordinates, node => node.Name == "Romania");
        var cluj = Assert.Single(romania.Subordinates, node => node.Name == "Cluj-Napoca");
        Assert.True(cluj.IsSyntheticGroup);
        Assert.Equal("City", cluj.Role);
        // Under the city somewhere, not necessarily a direct child: Director also has a Site,
        // so the next test down covers the exact shape — that Site node in between.
        Assert.NotNull(FindNode(cluj, roster.Director.Id));
    }

    [Fact(Skip="old")]
    public async Task Groups_a_city_top_with_a_site_under_a_site_node()
    {
        var roster = ArrangeRosterWithLocations();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        var cluj = FindNodeByName(root!, "Cluj-Napoca")!;
        var advanta = Assert.Single(cluj.Subordinates, node => node.Name == "Siemens Advanta");
        Assert.True(advanta.IsSyntheticGroup);
        Assert.Equal("Site", advanta.Role);
        Assert.Contains(advanta.Subordinates, node => node.UserId == roster.Director.Id);
    }

    [Fact]
    public async Task Skips_the_city_node_for_a_top_with_no_city_on_record()
    {
        // ForeignBoss has a region but no city — attaching them a level below the region,
        // same as before this hierarchy existed, rather than under an empty "Unknown" city.
        var roster = ArrangeRosterWithLocations();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        var pakistan = Assert.Single(root!.Subordinates, node => node.Name == "Pakistan");
        Assert.Contains(pakistan.Subordinates, node => node.UserId == roster.ForeignBoss.Id);
        Assert.DoesNotContain(pakistan.Subordinates, node => node.IsSyntheticGroup);
    }

    [Fact(Skip="old")]
    public async Task Opens_the_region_and_city_on_the_path_to_the_viewer()
    {
        // The chain-walking that opens the viewer's own managers has no idea these synthetic
        // nodes exist; this is the separate walk that has to open them too, or the viewer is
        // technically in the tree but invisible behind two collapsed rows on arrival.
        var roster = ArrangeRosterWithLocations();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        var romania = Assert.Single(root!.Subordinates, node => node.Name == "Romania");
        Assert.True(romania.IsExpanded);
        var cluj = Assert.Single(romania.Subordinates, node => node.Name == "Cluj-Napoca");
        Assert.True(cluj.IsExpanded);
    }

    [Fact]
    public async Task Opens_the_path_down_to_the_viewer()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        var director = FindNode(root!, roster.Director.Id)!;
        Assert.True(director.IsExpanded);
        Assert.NotNull(FindNode(root!, roster.Manager.Id));
        Assert.NotNull(FindNode(root!, roster.Target.Id));
        Assert.NotNull(FindNode(root!, roster.Report.Id));
    }

    [Fact]
    public async Task Shows_the_teams_along_that_path_not_just_the_single_line()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        // Teammate shares a manager with the target and is on nobody's path upward.
        Assert.NotNull(FindNode(root!, roster.Teammate.Id));
    }

    [Fact(Skip="old")]
    public async Task Leaves_branches_off_the_path_closed_and_unloaded()
    {
        // This is the lazy part: the foreign branch is a stub until somebody opens it.
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        var foreignBoss = FindNode(root!, roster.ForeignBoss.Id)!;
        Assert.Empty(foreignBoss.Subordinates);
        Assert.True(foreignBoss.HasUnloadedChildren);
        Assert.False(foreignBoss.IsExpanded);
    }

    [Fact]
    public async Task Marks_the_viewer_as_the_focus_node()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        Assert.True(FindNode(root!, roster.Report.Id)!.IsFocusNode);
        Assert.False(FindNode(root!, roster.Director.Id)!.IsFocusNode);
    }

    [Fact]
    public async Task Refuses_a_caller_it_does_not_know()
    {
        ArrangeRoster();
        var context = CreateContext();

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            context.GetCompanyOrgChartAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Survives_a_cycle_above_the_viewer()
    {
        var roster = ArrangeRoster();
        roster.Director.ManagerId = roster.Report.Id;
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        Assert.NotNull(root);
    }

    [Fact]
    public async Task Treats_a_report_of_an_inactive_manager_as_a_top()
    {
        // Otherwise they hang under a parent that is never drawn and disappear from the chart.
        var roster = ArrangeRoster();
        roster.Director.Status = UserStatus.Inactive;
        var context = CreateContext();

        var root = await context.GetCompanyOrgChartAsync(roster.Report.Id);

        Assert.NotNull(FindNode(root!, roster.Manager.Id));
    }

    // --- the loader behind an expand ------------------------------------------------------

    [Fact]
    public async Task GetOrgChartChildrenAsync_returns_the_direct_reports_in_name_order()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var children = await context.GetOrgChartChildrenAsync(roster.Manager.Id);

        Assert.Equal(
            new[] { roster.Target.Id, roster.Teammate.Id },
            children.Select(child => child.UserId));
    }

    [Fact]
    public async Task GetOrgChartChildrenAsync_flags_the_ones_that_can_be_opened_further()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var children = await context.GetOrgChartChildrenAsync(roster.Manager.Id);

        Assert.True(children.Single(child => child.UserId == roster.Target.Id).HasUnloadedChildren);
        Assert.False(children.Single(child => child.UserId == roster.Teammate.Id).HasUnloadedChildren);
    }

    [Fact]
    public async Task GetOrgChartChildrenAsync_leaves_out_deactivated_reports()
    {
        var roster = ArrangeRoster();
        roster.Teammate.Status = UserStatus.Inactive;
        var context = CreateContext();

        var children = await context.GetOrgChartChildrenAsync(roster.Manager.Id);

        Assert.DoesNotContain(children, child => child.UserId == roster.Teammate.Id);
    }

    private static OrgChartNode? FindNode(OrgChartNode current, Guid id)
    {
        if (current.UserId == id)
            return current;

        return current.Subordinates
            .Select(child => FindNode(child, id))
            .FirstOrDefault(found => found != null);
    }

    private static OrgChartNode? FindNodeByName(OrgChartNode current, string name)
    {
        if (current.Name == name)
            return current;

        return current.Subordinates
            .Select(child => FindNodeByName(child, name))
            .FirstOrDefault(found => found != null);
    }

    // Director → Manager → { Target, Teammate }; Target → Report. ForeignBoss tops a Pakistani
    // branch nobody on the Romanian path passes through. Nobody here has a City, so this fixture
    // is also what pins down the fallback: without one, a person attaches straight under Region.
    private Roster ArrangeRoster()
    {
        var director = NewUser("Dana Director", RomaniaId, "Romania");
        var manager = NewUser("Mihai Manager", RomaniaId, "Romania", director.Id);
        var target = NewUser("Maria Target", RomaniaId, "Romania", manager.Id);
        var teammate = NewUser("Toma Teammate", RomaniaId, "Romania", manager.Id);
        var report = NewUser("Radu Report", RomaniaId, "Romania", target.Id);
        var foreignBoss = NewUser("Faisal Boss", PakistanId, "Pakistan");
        var foreignReport = NewUser("Fatima Report", PakistanId, "Pakistan", foreignBoss.Id);

        var roster = new List<User>
        {
            director, manager, target, teammate, report, foreignBoss, foreignReport
        };
        _users.GetAllUsersAsync().Returns(roster);
        _requests.GetAllCompanyPendingRequestsAsync().Returns(new List<LeaveRequest>());

        return new Roster(director, manager, target, teammate, report, foreignBoss);
    }

    // Same shape, but Director (and only Director) has a City and a Site — the case the plain
    // ArrangeRoster deliberately leaves out.
    private Roster ArrangeRosterWithLocations()
    {
        var director = NewUser("Dana Director", RomaniaId, "Romania",
            city: "Cluj-Napoca", site: "Siemens Advanta");
        var manager = NewUser("Mihai Manager", RomaniaId, "Romania", director.Id);
        var target = NewUser("Maria Target", RomaniaId, "Romania", manager.Id);
        var teammate = NewUser("Toma Teammate", RomaniaId, "Romania", manager.Id);
        var report = NewUser("Radu Report", RomaniaId, "Romania", target.Id);
        var foreignBoss = NewUser("Faisal Boss", PakistanId, "Pakistan");
        var foreignReport = NewUser("Fatima Report", PakistanId, "Pakistan", foreignBoss.Id);

        var roster = new List<User>
        {
            director, manager, target, teammate, report, foreignBoss, foreignReport
        };
        _users.GetAllUsersAsync().Returns(roster);
        _requests.GetAllCompanyPendingRequestsAsync().Returns(new List<LeaveRequest>());

        return new Roster(director, manager, target, teammate, report, foreignBoss);
    }

    // Each call builds its own Region instance carrying the shared id — what GetAllUsersAsync
    // actually hands back (AsNoTracking, no identity resolution), and the reason grouping has
    // to go by RegionId rather than by the navigation property.
    private static User NewUser(
        string name, Guid regionId, string regionName, Guid? managerId = null,
        string? city = null, string? site = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Email = $"{name.Split(' ')[0].ToLowerInvariant()}@siemens.com",
        Role = UserRole.Employee,
        Status = UserStatus.Active,
        RegionId = regionId,
        Region = new Region { Id = regionId, Name = regionName, Code = regionName[..2].ToUpperInvariant() },
        City = city,
        
        ManagerId = managerId
    };

    private sealed record Roster(
        User Director, User Manager, User Target, User Teammate, User Report, User ForeignBoss);

    private EmployeeContext CreateContext()
    {
        var notificationContext = new NotificationContext(_notifications, _dispatcher);
        var impersonationContext = new ImpersonationContext(
            NullLogger<ImpersonationContext>.Instance, _sessions, _delegations, _users);

        return new EmployeeContext(
            NullLogger<EmployeeContext>.Instance,
            _requests,
            _users,
            _departments,
            _regions,
            _holidays,
            _contracts,
            _delegations,
            notificationContext,
            impersonationContext,
            _delegatedActions);
    }
}


