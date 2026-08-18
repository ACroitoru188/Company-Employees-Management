using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CompanyEmployees.Application.Tests;

// The directory is worldwide by design, so these cover what is left holding it together: the
// caller still has to exist, the pills still have to narrow, and the grouping has to describe
// real departments rather than one per employee.
public class EmployeeContextSearchTests
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

    private static readonly Region Romania = new() { Id = Guid.NewGuid(), Name = "Romania", Code = "RO" };
    private static readonly Region Pakistan = new() { Id = Guid.NewGuid(), Name = "Pakistan", Code = "PK" };

    private static readonly Department Design = new() { Id = Guid.NewGuid(), Name = "Design" };
    private static readonly Department Production = new() { Id = Guid.NewGuid(), Name = "Production" };

    [Fact]
    public async Task GlobalSearchAsync_shows_every_region_to_an_ordinary_employee()
    {
        // The directory is deliberately worldwide (2026-08-17). What stays region-scoped is
        // acting on somebody — decisions, contracts, rosters, dashboards and exports.
        var (caller, _) = ArrangeRoster(callerRole: UserRole.Employee, callerRegion: Romania);
        var context = CreateContext();

        var result = await context.GlobalSearchAsync(caller.Id, "a");

        Assert.Contains(result.People, person => person.Region == "Pakistan");
        Assert.Equal(2, result.Regions.Count);
    }

    [Fact]
    public async Task GlobalSearchAsync_refuses_a_caller_it_does_not_know()
    {
        // No filtering by the caller any more, so this is the only thing standing between an
        // unknown id and the whole company.
        ArrangeRoster(callerRole: UserRole.Employee, callerRegion: Romania);
        var context = CreateContext();

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            context.GlobalSearchAsync(Guid.NewGuid(), "a"));
    }

    [Fact]
    public async Task GlobalSearchAsync_narrows_to_a_pinned_foreign_region()
    {
        var (caller, _) = ArrangeRoster(callerRole: UserRole.Employee, callerRegion: Romania);
        var context = CreateContext();

        var result = await context.GlobalSearchAsync(caller.Id, query: null, regionId: Pakistan.Id,
            type: SearchEntityType.People);

        Assert.All(result.People, person => Assert.Equal("Pakistan", person.Region));
    }

    [Fact]
    public async Task GlobalSearchAsync_narrows_to_the_pinned_department()
    {
        var (caller, _) = ArrangeRoster(callerRole: UserRole.Admin, callerRegion: Romania);
        var context = CreateContext();

        var result = await context.GlobalSearchAsync(caller.Id, query: null, departmentId: Design.Id);

        Assert.NotEmpty(result.People);
        Assert.All(result.People, person => Assert.Equal("Design", person.Department));
    }

    [Fact]
    public async Task GlobalSearchAsync_offers_only_places_to_drill_into_before_anything_is_typed()
    {
        var (caller, _) = ArrangeRoster(callerRole: UserRole.Admin, callerRegion: Romania);
        var context = CreateContext();

        var result = await context.GlobalSearchAsync(caller.Id, query: null);

        Assert.Empty(result.People);
        Assert.NotEmpty(result.Regions);
        Assert.NotEmpty(result.Departments);
    }

    [Fact]
    public async Task GlobalSearchAsync_counts_every_type_whatever_the_chip_is_set_to()
    {
        // The chips are how the type is switched, so each has to report what is behind it
        // even while its own list is the one being suppressed.
        var (caller, _) = ArrangeRoster(callerRole: UserRole.Admin, callerRegion: Romania);
        var context = CreateContext();

        var result = await context.GlobalSearchAsync(
            caller.Id, "design", type: SearchEntityType.People);

        Assert.Empty(result.Departments);
        Assert.True(result.DepartmentsTotal > 0);
    }

    [Fact]
    public async Task GlobalSearchAsync_counts_a_departments_real_membership()
    {
        // A count that does not match what the drill-in produces sends people down dead ends.
        var (caller, _) = ArrangeRoster(callerRole: UserRole.Employee, callerRegion: Romania);
        var context = CreateContext();

        var result = await context.GlobalSearchAsync(caller.Id, "Design");

        var design = Assert.Single(result.Departments);
        Assert.Equal(2, design.MemberCount);
    }

    [Fact]
    public async Task GlobalSearchAsync_finds_a_department_by_its_managers_name()
    {
        // Department.Manager is only ever loaded by the department gateway; taken off a user's
        // Department navigation it is null, and the manager column renders empty for everyone.
        var (caller, _) = ArrangeRoster(callerRole: UserRole.Employee, callerRegion: Romania);
        var context = CreateContext();

        var result = await context.GlobalSearchAsync(caller.Id, "Dana");

        var design = Assert.Single(result.Departments);
        Assert.Equal("Design", design.Name);
        Assert.Equal("Dana Director", design.ManagerName);
    }

    [Fact]
    public async Task GlobalSearchAsync_groups_by_id_not_by_navigation_instance()
    {
        // The regression: grouping on user.Department grouped by *reference*, and the gateway
        // hands back one Department object per user, so four people in two departments came
        // back as four one-person departments — and every region as a one-person region.
        var (caller, _) = ArrangeRoster(callerRole: UserRole.Employee, callerRegion: Romania);
        var context = CreateContext();

        var result = await context.GlobalSearchAsync(caller.Id, query: null);

        Assert.Equal(2, result.DepartmentsTotal);
        Assert.Equal(2, result.RegionsTotal);
        Assert.Equal(3, result.Regions.Single(region => region.Name == "Romania").MemberCount);
    }

    [Fact]
    public async Task GlobalSearchAsync_lists_everyone_when_the_people_chip_is_pressed()
    {
        // The opening state hides people so the drill-in targets are what you see first, but
        // the chip reporting "People (4)" has to produce those four when it is pressed.
        var (caller, _) = ArrangeRoster(callerRole: UserRole.Employee, callerRegion: Romania);
        var context = CreateContext();

        var browsing = await context.GlobalSearchAsync(caller.Id, query: null);
        Assert.Empty(browsing.People);
        Assert.Equal(4, browsing.PeopleTotal);

        var chipped = await context.GlobalSearchAsync(
            caller.Id, query: null, type: SearchEntityType.People);

        Assert.Equal(4, chipped.People.Count);
    }

    [Fact]
    public async Task GlobalSearchAsync_leaves_out_deactivated_accounts()
    {
        var (caller, roster) = ArrangeRoster(callerRole: UserRole.Admin, callerRegion: Romania);
        roster.Single(user => user.Name == "Ana Popescu").Status = UserStatus.Inactive;
        var context = CreateContext();

        var result = await context.GlobalSearchAsync(caller.Id, "Ana");

        Assert.DoesNotContain(result.People, person => person.Name == "Ana Popescu");
    }

    // Two Romanians in Design, one Romanian in Production, one Pakistani in Production.
    private (User Caller, List<User> Roster) ArrangeRoster(UserRole callerRole, Region callerRegion)
    {
        var caller = NewUser("Ana Popescu", callerRole, callerRegion, Design);
        var roster = new List<User>
        {
            caller,
            NewUser("Andrei Ionescu", UserRole.Employee, Romania, Design),
            NewUser("Alina Marin", UserRole.LineManager, Romania, Production),
            NewUser("Pakistani Colleague", UserRole.Employee, Pakistan, Production)
        };

        _users.GetAllUsersAsync().Returns(roster);
        _users.GetUserByIdAsync(caller.Id).Returns(caller);

        // Only this gateway loads Department.Manager, which is why the hit's manager name is
        // taken from here rather than off a user's Department navigation.
        _departments.GetAllAsync().Returns(new List<Department>
        {
            new() { Id = Design.Id, Name = Design.Name, Manager = new User { Name = "Dana Director" } },
            new() { Id = Production.Id, Name = Production.Name }
        });

        return (caller, roster);
    }

    // Each user gets its **own** Region and Department objects carrying the shared ids, which
    // is what the real gateway hands back: GetAllUsersAsync reads AsNoTracking, and without
    // identity resolution EF materialises a separate instance of each navigation per parent
    // row. Sharing one instance in the fixture hid a grouping-by-reference bug that turned
    // every employee into their own one-person department in the running app.
    private static User NewUser(string name, UserRole role, Region region, Department department) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Email = $"{name.Split(' ')[0].ToLowerInvariant()}@siemens.com",
        Role = role,
        Status = UserStatus.Active,
        RegionId = region.Id,
        Region = new Region { Id = region.Id, Name = region.Name, Code = region.Code },
        DepartmentId = department.Id,
        Department = new Department { Id = department.Id, Name = department.Name }
    };

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
