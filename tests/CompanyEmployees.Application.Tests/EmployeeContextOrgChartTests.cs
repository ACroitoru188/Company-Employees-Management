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
        Assert.Contains(root.Subordinates, node => node.UserId == roster.Director.Id);
        Assert.Contains(root.Subordinates, node => node.UserId == roster.ForeignBoss.Id);
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

    [Fact]
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

        Assert.Contains(root!.Subordinates, node => node.UserId == roster.Manager.Id);
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

    // Director → Manager → { Maria Target, Toma Teammate }; Target → Report. ForeignBoss tops a
    // Pakistani branch nobody on the Romanian path passes through.
    private Roster ArrangeRoster()
    {
        var director = NewUser("Dana Director", RomaniaId);
        var manager = NewUser("Mihai Manager", RomaniaId, director.Id);
        var target = NewUser("Maria Target", RomaniaId, manager.Id);
        var teammate = NewUser("Toma Teammate", RomaniaId, manager.Id);
        var report = NewUser("Radu Report", RomaniaId, target.Id);
        var foreignBoss = NewUser("Faisal Boss", PakistanId);
        var foreignReport = NewUser("Fatima Report", PakistanId, foreignBoss.Id);

        var roster = new List<User>
        {
            director, manager, target, teammate, report, foreignBoss, foreignReport
        };
        _users.GetAllUsersAsync().Returns(roster);
        _requests.GetAllCompanyPendingRequestsAsync().Returns(new List<LeaveRequest>());

        return new Roster(director, manager, target, teammate, report, foreignBoss);
    }

    private static User NewUser(string name, Guid regionId, Guid? managerId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Email = $"{name.Split(' ')[0].ToLowerInvariant()}@siemens.com",
        Role = UserRole.Employee,
        Status = UserStatus.Active,
        RegionId = regionId,
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
