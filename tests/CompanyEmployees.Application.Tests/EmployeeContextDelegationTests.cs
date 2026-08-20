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

    private static User NewUser(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Email = $"{name.Split(' ')[0].ToLowerInvariant()}@siemens.com",
        Role = UserRole.Employee,
        Status = UserStatus.Active,
        Region = Romania,
        RegionId = Romania.Id
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
