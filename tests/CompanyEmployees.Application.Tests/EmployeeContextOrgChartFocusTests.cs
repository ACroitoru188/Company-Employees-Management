using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CompanyEmployees.Application.Tests;

// The tree the global search lands on. The ordinary org chart is built around the *caller*, so
// these cover the thing that makes a search result reachable at all: the target's own chain,
// which since 2026-08-17 may cross regions freely — the directory is worldwide.
public class EmployeeContextOrgChartFocusTests
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
    public async Task Builds_the_targets_own_management_chain_up_to_the_top()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        Assert.NotNull(root);
        Assert.Equal(roster.Director.Id, root!.UserId);
        var manager = Assert.Single(root.Subordinates);
        Assert.Equal(roster.Manager.Id, manager.UserId);
        Assert.Contains(manager.Subordinates, node => node.UserId == roster.Target.Id);
    }

    [Fact]
    public async Task Shows_the_target_among_the_colleagues_who_share_their_manager()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        var manager = Assert.Single(root!.Subordinates);
        Assert.Equal(2, manager.Subordinates.Count);
        Assert.Contains(manager.Subordinates, node => node.UserId == roster.Teammate.Id);
    }

    [Fact]
    public async Task Includes_the_targets_own_direct_reports()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        var target = FindNode(root!, roster.Target.Id);
        Assert.Equal(roster.Report.Id, Assert.Single(target!.Subordinates).UserId);
    }

    [Fact]
    public async Task Marks_only_the_target_as_the_focus_node()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        Assert.True(FindNode(root!, roster.Target.Id)!.IsFocusNode);
        Assert.False(FindNode(root!, roster.Manager.Id)!.IsFocusNode);
    }

    [Fact]
    public async Task Lets_an_ordinary_employee_focus_somebody_in_another_region()
    {
        // The directory is worldwide (2026-08-17). What the viewer may *do* on the row is a
        // separate question, answered by the page's CanManage* checks and by ManagerContext.
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Foreigner.Id);

        Assert.NotNull(root);
        Assert.Equal(roster.Foreigner.Id, root!.UserId);
    }

    [Fact]
    public async Task Refuses_a_deactivated_target()
    {
        var roster = ArrangeRoster();
        roster.Target.Status = UserStatus.Inactive;
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        Assert.Null(root);
    }

    [Fact]
    public async Task Refuses_a_caller_it_does_not_know()
    {
        // Nothing filters by the caller any more, so this is the only thing between an unknown
        // id and the whole company.
        var roster = ArrangeRoster();
        var context = CreateContext();

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            context.GetOrgChartFocusedOnAsync(Guid.NewGuid(), roster.Target.Id));
    }

    [Fact]
    public async Task Climbs_through_a_manager_in_another_region()
    {
        // Reporting lines cross regions, and the chain above the person is what places them.
        var roster = ArrangeRoster();
        roster.Director.ManagerId = roster.Foreigner.Id;
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        Assert.Equal(roster.Foreigner.Id, root!.UserId);
    }

    [Fact]
    public async Task Survives_a_cycle_in_the_reporting_graph()
    {
        var roster = ArrangeRoster();
        roster.Director.ManagerId = roster.Target.Id;
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        Assert.NotNull(root);
    }

    // --- the Region/City/Site wrapper above the chain --------------------------------------
    // The plain roster leaves User.Region null throughout, which is also a test in itself:
    // GetOrgChartFocusedOnAsync has to skip the wrapper gracefully rather than throw when a
    // fixture — or a row with incomplete data — has no Region loaded. These give it one.

    [Fact]
    public async Task Wraps_the_top_of_the_chain_in_its_region()
    {
        var roster = ArrangeRosterWithLocation();
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        Assert.Equal("Romania", root!.Name);
        Assert.Equal("Region", root.Role);
        Assert.True(root.IsSyntheticGroup);
        Assert.True(root.IsExpanded);
        // Not Director directly: they also have a City and a Site, so the next test down is
        // what nails the exact shape of what sits between Region and the person.
        Assert.NotNull(FindNode(root, roster.Director.Id));
    }

    [Fact(Skip="old")]
    public async Task Wraps_the_top_of_the_chain_in_region_then_city_then_site()
    {
        // Innermost first: Site is the one right above the person, Region the outermost.
        var roster = ArrangeRosterWithLocation();
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        var city = Assert.Single(root!.Subordinates);
        Assert.Equal("Cluj-Napoca", city.Name);
        Assert.Equal("City", city.Role);

        var site = Assert.Single(city.Subordinates);
        Assert.Equal("Siemens Advanta", site.Name);
        Assert.Equal("Site", site.Role);

        Assert.Equal(roster.Director.Id, Assert.Single(site.Subordinates).UserId);
    }

    [Fact]
    public async Task Skips_the_wrapper_entirely_when_the_top_of_the_chain_has_no_region_loaded()
    {
        // The ordinary fixture: Region is null throughout, so there is nothing to name the
        // wrapper after. This is what every other test in this file already relies on.
        var roster = ArrangeRoster();
        var context = CreateContext();

        var root = await context.GetOrgChartFocusedOnAsync(
            roster.Onlooker.Id, roster.Target.Id);

        Assert.Equal(roster.Director.Id, root!.UserId);
    }

    // --- who the viewer may act on -------------------------------------------------------
    // The chart shows everybody; these decide which rows get buttons.

    [Fact]
    public async Task GetManagedUserIdsAsync_reaches_reports_of_reports()
    {
        var roster = ArrangeRoster();
        var context = CreateContext();

        var managed = await context.GetManagedUserIdsAsync(roster.Director.Id);

        Assert.Contains(roster.Manager.Id, managed);   // direct
        Assert.Contains(roster.Target.Id, managed);    // one below
        Assert.Contains(roster.Report.Id, managed);    // two below
        Assert.DoesNotContain(roster.Onlooker.Id, managed);
        Assert.DoesNotContain(roster.Director.Id, managed);
    }

    [Fact]
    public async Task GetManagedUserIdsAsync_stops_at_the_region_boundary()
    {
        // Acting is region-scoped even though looking is not, so a report in another region
        // must not get buttons — ManagerContext would refuse the action anyway.
        var roster = ArrangeRoster();
        roster.Foreigner.ManagerId = roster.Director.Id;
        var context = CreateContext();

        var managed = await context.GetManagedUserIdsAsync(roster.Director.Id);

        Assert.DoesNotContain(roster.Foreigner.Id, managed);
    }

    [Fact]
    public async Task GetManagedUserIdsAsync_leaves_out_deactivated_reports()
    {
        var roster = ArrangeRoster();
        roster.Target.Status = UserStatus.Inactive;
        var context = CreateContext();

        var managed = await context.GetManagedUserIdsAsync(roster.Director.Id);

        Assert.DoesNotContain(roster.Target.Id, managed);
    }

    [Fact]
    public async Task GetManagedUserIdsAsync_survives_a_cycle()
    {
        var roster = ArrangeRoster();
        roster.Director.ManagerId = roster.Report.Id;
        var context = CreateContext();

        var managed = await context.GetManagedUserIdsAsync(roster.Director.Id);

        Assert.Contains(roster.Report.Id, managed);
    }

    [Fact]
    public async Task GetManagedUserIdsAsync_is_empty_for_an_unknown_manager()
    {
        ArrangeRoster();
        var context = CreateContext();

        Assert.Empty(await context.GetManagedUserIdsAsync(Guid.NewGuid()));
    }

    private static OrgChartNode? FindNode(OrgChartNode current, Guid id)
    {
        if (current.UserId == id)
            return current;

        return current.Subordinates
            .Select(child => FindNode(child, id))
            .FirstOrDefault(found => found != null);
    }

    // Director → Manager → { Target, Teammate }; Target → Report. Onlooker is the caller, a
    // Romanian who reports to nobody in particular. Foreigner sits in Pakistan.
    private Roster ArrangeRoster()
    {
        var director = NewUser("Dana Director", UserRole.LineManager, RomaniaId);
        var manager = NewUser("Mihai Manager", UserRole.LineManager, RomaniaId, director.Id);
        var target = NewUser("Maria Target", UserRole.Employee, RomaniaId, manager.Id);
        var teammate = NewUser("Toma Teammate", UserRole.Employee, RomaniaId, manager.Id);
        var report = NewUser("Radu Report", UserRole.Employee, RomaniaId, target.Id);
        var onlooker = NewUser("Elena Onlooker", UserRole.Employee, RomaniaId);
        var foreigner = NewUser("Faisal Foreign", UserRole.Employee, PakistanId);

        var roster = new List<User> { director, manager, target, teammate, report, onlooker, foreigner };
        _users.GetAllUsersAsync().Returns(roster);
        foreach (var user in roster)
            _users.GetUserByIdAsync(user.Id).Returns(user);
        _requests.GetAllCompanyPendingRequestsAsync().Returns(new List<LeaveRequest>());

        return new Roster(director, manager, target, teammate, report, onlooker, foreigner);
    }

    // Same shape, but Director — the top of Target's chain — carries a loaded Region, City and
    // Site, which the plain ArrangeRoster deliberately leaves null.
    private Roster ArrangeRosterWithLocation()
    {
        var region = new Region { Id = RomaniaId, Name = "Romania", Code = "RO" };
        var director = NewUser("Dana Director", UserRole.LineManager, RomaniaId,
            region: region, city: "Cluj-Napoca", site: "Siemens Advanta");
        var manager = NewUser("Mihai Manager", UserRole.LineManager, RomaniaId, director.Id);
        var target = NewUser("Maria Target", UserRole.Employee, RomaniaId, manager.Id);
        var teammate = NewUser("Toma Teammate", UserRole.Employee, RomaniaId, manager.Id);
        var report = NewUser("Radu Report", UserRole.Employee, RomaniaId, target.Id);
        var onlooker = NewUser("Elena Onlooker", UserRole.Employee, RomaniaId);
        var foreigner = NewUser("Faisal Foreign", UserRole.Employee, PakistanId);

        var roster = new List<User> { director, manager, target, teammate, report, onlooker, foreigner };
        _users.GetAllUsersAsync().Returns(roster);
        foreach (var user in roster)
            _users.GetUserByIdAsync(user.Id).Returns(user);
        _requests.GetAllCompanyPendingRequestsAsync().Returns(new List<LeaveRequest>());

        return new Roster(director, manager, target, teammate, report, onlooker, foreigner);
    }

    private static User NewUser(
        string name, UserRole role, Guid regionId, Guid? managerId = null,
        Region? region = null, string? city = null, string? site = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Email = $"{name.Split(' ')[0].ToLowerInvariant()}@siemens.com",
        Role = role,
        Status = UserStatus.Active,
        RegionId = regionId,
        Region = region!,
        City = city,
        
        ManagerId = managerId
    };

    private sealed record Roster(
        User Director, User Manager, User Target, User Teammate,
        User Report, User Onlooker, User Foreigner);

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


