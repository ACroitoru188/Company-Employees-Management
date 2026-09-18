using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CompanyEmployees.Application.Tests;

// Borrowing an ordinary employee's account leaves exactly one mark: a leave request in their
// name. These cover the two things that has to keep doing — refuse once the delegation stops
// being good, and record who was actually behind it when it is.
public class EmployeeContextDelegationTests
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

    [Fact]
    public async Task SubmitRequestAsync_refuses_an_expired_delegation_before_writing_anything()
    {
        // The auth cookie outlives the delegation window, so this is the check that has to
        // bite the moment the window closes rather than at the next sign-in.
        var setup = Arrange(delegationEnd: DateOnly.FromDateTime(DateTime.Today).AddDays(-1));
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() => context.SubmitRequestAsync(
            setup.Owner.Id, LeaveType.Annual, Tomorrow, Tomorrow, "Dentist",
            new ActingOnBehalf(setup.Delegate.Id, setup.Delegation.Id)));

        await _requests.DidNotReceive().CreateRequestAsync(Arg.Any<LeaveRequest>());
        await _delegatedActions.DidNotReceive().CreateAsync(Arg.Any<DelegatedAction>());
    }

    [Fact]
    public async Task SubmitRequestAsync_refuses_a_delegation_given_to_somebody_else()
    {
        var setup = Arrange();
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() => context.SubmitRequestAsync(
            setup.Owner.Id, LeaveType.Annual, Tomorrow, Tomorrow, "Dentist",
            new ActingOnBehalf(RealUserId: Guid.NewGuid(), setup.Delegation.Id)));

        await _requests.DidNotReceive().CreateRequestAsync(Arg.Any<LeaveRequest>());
    }

    [Fact]
    public async Task SubmitRequestAsync_refuses_a_delegation_for_a_different_account()
    {
        // A stale cookie can name a delegation that covers somebody else entirely.
        var setup = Arrange();
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() => context.SubmitRequestAsync(
            userId: Guid.NewGuid(), LeaveType.Annual, Tomorrow, Tomorrow, "Dentist",
            new ActingOnBehalf(setup.Delegate.Id, setup.Delegation.Id)));

        await _requests.DidNotReceive().CreateRequestAsync(Arg.Any<LeaveRequest>());
    }

    [Fact]
    public async Task SubmitRequestAsync_records_the_human_behind_a_borrowed_request()
    {
        var setup = Arrange();
        var context = CreateContext();

        await context.SubmitRequestAsync(
            setup.Owner.Id, LeaveType.Annual, Tomorrow, Tomorrow, "Dentist",
            new ActingOnBehalf(setup.Delegate.Id, setup.Delegation.Id));

        await _delegatedActions.Received(1).CreateAsync(Arg.Is<DelegatedAction>(action =>
            action != null
            && action.RealUserId == setup.Delegate.Id
            && action.ActedAsUserId == setup.Owner.Id
            && action.TargetUserId == setup.Owner.Id
            && action.ActionType == DelegatedActionType.LeaveRequested
            && action.DelegationId == setup.Delegation.Id));
    }

    [Fact]
    public async Task SubmitRequestAsync_writes_no_audit_row_when_nobody_is_borrowing()
    {
        // Acting as yourself is the ordinary case and must stay out of the delegation trail.
        var setup = Arrange();
        var context = CreateContext();

        await context.SubmitRequestAsync(
            setup.Owner.Id, LeaveType.Annual, Tomorrow, Tomorrow, "Dentist");

        await _requests.Received(1).CreateRequestAsync(Arg.Any<LeaveRequest>());
        await _delegatedActions.DidNotReceive().CreateAsync(Arg.Any<DelegatedAction>());
    }

    [Fact]
    public async Task HrDecideRequestAsync_records_delegated_action_when_acting_on_behalf()
    {
        var setup = ArrangeAdmin();
        var manager = NewUser("Mihai Manager", UserRole.LineManager);
        var employee = NewUser("Ion Angajat");
        employee.Manager = manager;
        employee.ManagerId = manager.Id;

        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            User = employee,
            Type = LeaveType.Annual,
            StartDate = Tomorrow,
            EndDate = Tomorrow.AddDays(2),
            Status = LeaveStatus.Pending,
            Approvals = new List<LeaveApproval>()
        };

        _requests.GetRequestByIdAsync(request.Id).Returns(request);
        var context = CreateContext();

        await context.HrDecideRequestAsync(
            setup.Admin.Id, request.Id, approve: true,
            new ActingOnBehalf(setup.Delegate.Id, setup.Delegation.Id));

        await _requests.Received(1).SaveDecisionAsync(request, Arg.Any<LeaveApproval>());
        await _delegatedActions.Received(1).CreateAsync(Arg.Is<DelegatedAction>(action =>
            action != null
            && action.RealUserId == setup.Delegate.Id
            && action.ActedAsUserId == setup.Admin.Id
            && action.TargetUserId == employee.Id
            && action.ActionType == DelegatedActionType.LeaveApproved
            && action.DelegationId == setup.Delegation.Id));
    }

    [Fact]
    public async Task HrDecideRequestAsync_refuses_an_expired_delegation()
    {
        var setup = ArrangeAdmin(delegationEnd: DateOnly.FromDateTime(DateTime.Today).AddDays(-1));
        var employee = NewUser("Ion Angajat");
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            User = employee,
            Type = LeaveType.Annual,
            StartDate = Tomorrow,
            EndDate = Tomorrow.AddDays(2),
            Status = LeaveStatus.Pending,
            Approvals = new List<LeaveApproval>()
        };

        _requests.GetRequestByIdAsync(request.Id).Returns(request);
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() => context.HrDecideRequestAsync(
            setup.Admin.Id, request.Id, approve: true,
            new ActingOnBehalf(setup.Delegate.Id, setup.Delegation.Id)));

        await _requests.DidNotReceiveWithAnyArgs().SaveDecisionAsync(default!, default!);
        await _delegatedActions.DidNotReceive().CreateAsync(Arg.Any<DelegatedAction>());
    }

    [Fact]
    public async Task AssignUserToDepartmentAsync_records_delegated_action_when_acting_on_behalf()
    {
        var setup = ArrangeAdmin();
        var employee = NewUser("Ion Angajat");
        var engineering = new Department { Id = Guid.NewGuid(), Name = "Engineering" };

        _users.GetUserByIdAsync(employee.Id).Returns(employee);
        _departments.GetByIdAsync(engineering.Id).Returns(engineering);
        var context = CreateAdminContext();

        await context.AssignUserToDepartmentAsync(
            setup.Admin.Id, employee.Id, engineering.Id,
            new ActingOnBehalf(setup.Delegate.Id, setup.Delegation.Id));

        await _users.Received(1).UpdateUserAsync(Arg.Is<User>(u => u.Id == employee.Id && u.DepartmentId == engineering.Id));
        await _delegatedActions.Received(1).CreateAsync(Arg.Is<DelegatedAction>(action =>
            action != null
            && action.RealUserId == setup.Delegate.Id
            && action.ActedAsUserId == setup.Admin.Id
            && action.TargetUserId == employee.Id
            && action.ActionType == DelegatedActionType.DepartmentChanged
            && action.Details != null && action.Details.Contains("Engineering")));
    }

    [Fact]
    public async Task SaveUserContractAsync_records_ContractExtended_when_extending_end_date()
    {
        var setup = ArrangeAdmin();
        var employee = NewUser("Ion Angajat");
        var activeContract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            Type = ContractType.Determinate,
            Status = ContractStatus.Active,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 6, 30)
        };

        _users.GetUserByIdAsync(employee.Id).Returns(employee);
        _contracts.GetActiveContractByUserIdAsync(employee.Id).Returns(activeContract);
        var context = CreateContractContext();

        var newEndDate = new DateOnly(2026, 12, 31);
        await context.SaveUserContractAsync(
            setup.Admin.Id, employee.Id, ContractType.Determinate, ContractStatus.Active,
            activeContract.StartDate, newEndDate, "Extension",
            new ActingOnBehalf(setup.Delegate.Id, setup.Delegation.Id));

        await _contracts.Received(1).UpdateAsync(Arg.Is<Contract>(c => c.Id == activeContract.Id && c.EndDate == newEndDate));
        await _delegatedActions.Received(1).CreateAsync(Arg.Is<DelegatedAction>(action =>
            action != null
            && action.RealUserId == setup.Delegate.Id
            && action.ActedAsUserId == setup.Admin.Id
            && action.TargetUserId == employee.Id
            && action.ActionType == DelegatedActionType.ContractExtended
            && action.Details != null && action.Details.Contains("2026-12-31")));
    }

    [Fact]
    public async Task SaveUserContractAsync_records_ContractUpdated_when_updating_contract_details()
    {
        var setup = ArrangeAdmin();
        var employee = NewUser("Ion Angajat");
        var activeContract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            Type = ContractType.Indeterminate,
            Status = ContractStatus.Active,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = null
        };

        _users.GetUserByIdAsync(employee.Id).Returns(employee);
        _contracts.GetActiveContractByUserIdAsync(employee.Id).Returns(activeContract);
        var context = CreateContractContext();

        await context.SaveUserContractAsync(
            setup.Admin.Id, employee.Id, ContractType.Indeterminate, ContractStatus.Active,
            activeContract.StartDate, null, "Updated notes",
            new ActingOnBehalf(setup.Delegate.Id, setup.Delegation.Id));

        await _contracts.Received(1).UpdateAsync(Arg.Is<Contract>(c => c.Id == activeContract.Id && c.Notes == "Updated notes"));
        await _delegatedActions.Received(1).CreateAsync(Arg.Is<DelegatedAction>(action =>
            action != null
            && action.RealUserId == setup.Delegate.Id
            && action.ActedAsUserId == setup.Admin.Id
            && action.TargetUserId == employee.Id
            && action.ActionType == DelegatedActionType.ContractUpdated));
    }

    [Fact]
    public async Task AssignUserToRegionAsync_records_delegated_action_when_acting_on_behalf()
    {
        var setup = ArrangeAdmin();
        var employee = NewUser("Ion Angajat");
        var germany = new Region { Id = Guid.NewGuid(), Name = "Germany", Code = "DE", IsActive = true };

        _users.GetUserByIdAsync(employee.Id).Returns(employee);
        _regions.GetByIdAsync(germany.Id).Returns(germany);
        _users.GetAllDirectReportsAsync(employee.Id).Returns(new List<User>());
        var context = CreateAdminContext();

        await context.AssignUserToRegionAsync(
            setup.Admin.Id, employee.Id, germany.Id,
            new ActingOnBehalf(setup.Delegate.Id, setup.Delegation.Id));

        await _users.Received(1).UpdateUserAsync(Arg.Is<User>(u => u.Id == employee.Id && u.RegionId == germany.Id));
        await _delegatedActions.Received(1).CreateAsync(Arg.Is<DelegatedAction>(action =>
            action != null
            && action.RealUserId == setup.Delegate.Id
            && action.ActedAsUserId == setup.Admin.Id
            && action.TargetUserId == employee.Id
            && action.ActionType == DelegatedActionType.RegionChanged
            && action.Details != null && action.Details.Contains("Germany")));
    }

    private static DateOnly Tomorrow => NextWorkingDay(DateOnly.FromDateTime(DateTime.Today).AddDays(1));

    // Leave must start and end on a working day, so a run on a Friday must not pick Saturday.
    private static DateOnly NextWorkingDay(DateOnly day)
    {
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            day = day.AddDays(1);
        return day;
    }

    private (User Owner, User Delegate, ManagerDelegation Delegation) Arrange(DateOnly? delegationEnd = null)
    {
        var owner = NewUser("Ana Popescu");
        var stand_in = NewUser("Andrei Ionescu");

        var delegation = new ManagerDelegation
        {
            Id = Guid.NewGuid(),
            ManagerId = owner.Id,
            Manager = owner,
            DelegateId = stand_in.Id,
            Delegate = stand_in,
            StartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-1),
            EndDate = delegationEnd ?? DateOnly.FromDateTime(DateTime.Today).AddDays(7),
            IsActive = true
        };

        _users.GetUserByIdAsync(owner.Id).Returns(owner);
        _users.GetUserByIdAsync(stand_in.Id).Returns(stand_in);
        _delegations.GetByIdAsync(delegation.Id).Returns(delegation);

        _requests.GetRequestsByUserAsync(owner.Id).Returns(new List<LeaveRequest>());
        _contracts.GetContractsByUserIdAsync(owner.Id).Returns(new List<Contract>());
        _holidays.GetHolidaysAsync(Arg.Any<string>(), Arg.Any<int>())
            .Returns(Array.Empty<PublicHoliday>());

        // Enough of an allocation that the balance check is not what fails the test.
        _requests.GetAllocationsByUserAsync(owner.Id, Arg.Any<int>()).Returns(new List<LeaveAllocation>
        {
            new() { UserId = owner.Id, LeaveType = LeaveType.Annual, Year = Tomorrow.Year, NumberOfDays = 21 }
        });

        return (owner, stand_in, delegation);
    }

    private (User Admin, User Delegate, ManagerDelegation Delegation) ArrangeAdmin(DateOnly? delegationEnd = null)
    {
        var admin = NewUser("Paul Rusu", UserRole.Admin);
        var stand_in = NewUser("Demo Employee", UserRole.Employee);

        var delegation = new ManagerDelegation
        {
            Id = Guid.NewGuid(),
            ManagerId = admin.Id,
            Manager = admin,
            DelegateId = stand_in.Id,
            Delegate = stand_in,
            StartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-1),
            EndDate = delegationEnd ?? DateOnly.FromDateTime(DateTime.Today).AddDays(7),
            IsActive = true
        };

        _users.GetUserByIdAsync(admin.Id).Returns(admin);
        _users.GetUserByIdAsync(stand_in.Id).Returns(stand_in);
        _delegations.GetByIdAsync(delegation.Id).Returns(delegation);

        return (admin, stand_in, delegation);
    }

    private static User NewUser(string name, UserRole role = UserRole.Employee) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Email = $"{name.Split(' ')[0].ToLowerInvariant()}@siemens.com",
        Role = role,
        Status = UserStatus.Active,
        Region = Romania,
        RegionId = Romania.Id
    };

    private LeaveContext CreateContext()
    {
        var notificationContext = new NotificationContext(_notifications, _dispatcher);
        var impersonationContext = new ImpersonationContext(
            NullLogger<ImpersonationContext>.Instance, _sessions, _delegations, _users);
        var delegationGuard = new DelegationGuard(impersonationContext, _delegatedActions);

        return new LeaveContext(
            NullLogger<LeaveContext>.Instance,
            _requests,
            _users,
            _contracts,
            _delegations,
            _holidays,
            notificationContext,
            delegationGuard);
    }

    private AdminContext CreateAdminContext()
    {
        var impersonationContext = new ImpersonationContext(
            NullLogger<ImpersonationContext>.Instance, _sessions, _delegations, _users);
        var delegationGuard = new DelegationGuard(impersonationContext, _delegatedActions);

        return new AdminContext(
            NullLogger<AdminContext>.Instance,
            _users,
            _departments,
            _regions,
            delegationGuard);
    }

    private ContractContext CreateContractContext()
    {
        var notificationContext = new NotificationContext(_notifications, _dispatcher);
        var impersonationContext = new ImpersonationContext(
            NullLogger<ImpersonationContext>.Instance, _sessions, _delegations, _users);
        var delegationGuard = new DelegationGuard(impersonationContext, _delegatedActions);

        return new ContractContext(
            NullLogger<ContractContext>.Instance,
            _contracts,
            _users,
            _delegations,
            notificationContext,
            delegationGuard);
    }
}
