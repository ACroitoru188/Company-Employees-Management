using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainInvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Tests;

// Undoing approved leave is a two-step flow: the employee asks, HR answers. The thing that
// must not slip is the balance — days stay spent for as long as the answer is outstanding,
// because the balance counts Approved rows and a request that may be refused must not free
// them early. Everything below exists to hold that line and the region/ownership guards.
public class EmployeeContextCancellationRequestTests
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

    private static readonly Department HrDepartment = new()
    {
        Id = Guid.NewGuid(),
        Name = LeaveApprovalPolicy.HrDepartmentName
    };

    private static readonly Department Design = new() { Id = Guid.NewGuid(), Name = "Design" };

    private readonly List<User> _everyone = new();

    public EmployeeContextCancellationRequestTests() =>
        _users.GetAllUsersAsync().Returns(_ => _everyone);

    // --- the employee asking ----------------------------------------------------------

    [Fact]
    public async Task RequestCancellation_marks_the_request_without_freeing_the_days()
    {
        var employee = NewUser("Ion Angajat");
        var request = ApprovedRequest(employee);
        var context = CreateContext();

        await context.RequestCancellationAsync(employee.Id, request.Id, "Deadline moved.");

        // The whole point: still Approved, so the balance has not given anything back yet.
        Assert.Equal(LeaveStatus.Approved, request.Status);
        Assert.NotNull(request.CancellationRequestedAt);
        Assert.Equal("Deadline moved.", request.CancellationReason);
        await _requests.Received(1).CancelRequestAsync(request);
    }

    [Fact]
    public async Task RequestCancellation_trims_the_reason()
    {
        var employee = NewUser("Ion Angajat");
        var request = ApprovedRequest(employee);
        var context = CreateContext();

        await context.RequestCancellationAsync(employee.Id, request.Id, "   Deadline moved.   ");

        Assert.Equal("Deadline moved.", request.CancellationReason);
    }

    [Fact]
    public async Task RequestCancellation_refuses_an_empty_reason()
    {
        var employee = NewUser("Ion Angajat");
        var request = ApprovedRequest(employee);
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.RequestCancellationAsync(employee.Id, request.Id, "   "));

        Assert.Null(request.CancellationRequestedAt);
        await _requests.DidNotReceiveWithAnyArgs().CancelRequestAsync(default!);
    }

    [Fact]
    public async Task RequestCancellation_refuses_somebody_elses_request()
    {
        var employee = NewUser("Ion Angajat");
        var stranger = NewUser("Ana Popescu");
        var request = ApprovedRequest(employee);
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.RequestCancellationAsync(stranger.Id, request.Id, "Deadline moved."));

        await _requests.DidNotReceiveWithAnyArgs().CancelRequestAsync(default!);
    }

    [Fact]
    public async Task RequestCancellation_refuses_a_pending_request()
    {
        // Nobody has approved it, so it is withdrawn outright — that is CancelRequestAsync.
        var employee = NewUser("Ion Angajat");
        var request = ApprovedRequest(employee);
        request.Status = LeaveStatus.Pending;
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.RequestCancellationAsync(employee.Id, request.Id, "Deadline moved."));

        await _requests.DidNotReceiveWithAnyArgs().CancelRequestAsync(default!);
    }

    [Fact]
    public async Task RequestCancellation_refuses_a_second_request_while_one_is_open()
    {
        var employee = NewUser("Ion Angajat");
        var request = ApprovedRequest(employee);
        request.CancellationRequestedAt = DateTime.UtcNow.AddHours(-1);
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.RequestCancellationAsync(employee.Id, request.Id, "Asking again."));

        await _requests.DidNotReceiveWithAnyArgs().CancelRequestAsync(default!);
    }

    [Fact]
    public async Task RequestCancellation_refuses_leave_that_has_already_ended()
    {
        // Days that were served out cannot be handed back.
        var employee = NewUser("Ion Angajat");
        var request = ApprovedRequest(employee);
        request.StartDate = Today.AddDays(-10);
        request.EndDate = Today.AddDays(-5);
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.RequestCancellationAsync(employee.Id, request.Id, "Too late."));

        await _requests.DidNotReceiveWithAnyArgs().CancelRequestAsync(default!);
    }

    [Fact]
    public async Task RequestCancellation_allows_leave_that_is_under_way()
    {
        var employee = NewUser("Ion Angajat");
        var request = ApprovedRequest(employee);
        request.StartDate = Today.AddDays(-2);
        request.EndDate = Today.AddDays(2);
        var context = CreateContext();

        await context.RequestCancellationAsync(employee.Id, request.Id, "Cut it short.");

        Assert.NotNull(request.CancellationRequestedAt);
    }

    [Fact]
    public async Task RequestCancellation_notifies_HR_in_the_employees_own_region()
    {
        var employee = NewUser("Ion Angajat");
        var localHr = NewHrUser("Elena HR");
        var foreignHr = NewHrUser("Ahmed HR", Pakistan);
        var request = ApprovedRequest(employee);
        var context = CreateContext();

        await context.RequestCancellationAsync(employee.Id, request.Id, "Deadline moved.");

        await _notifications.Received(1).CreateNotificationAsync(Arg.Is<Notification>(n =>
            n != null && n.UserId == localHr.Id && n.Message.Contains("Ion Angajat asked to cancel")));
        await _notifications.DidNotReceive().CreateNotificationAsync(Arg.Is<Notification>(n =>
            n != null && n.UserId == foreignHr.Id));
    }

    [Fact]
    public async Task RequestCancellation_gives_the_requester_a_second_person_receipt()
    {
        var employee = NewUser("Ion Angajat");
        NewHrUser("Elena HR");
        var request = ApprovedRequest(employee);
        var context = CreateContext();

        await context.RequestCancellationAsync(employee.Id, request.Id, "Deadline moved.");

        await _notifications.Received(1).CreateNotificationAsync(Arg.Is<Notification>(n =>
            n != null
            && n.UserId == employee.Id
            && n.Message.StartsWith("You asked HR to cancel your approved")
            && n.Message.Contains("Deadline moved.")
            && n.ActionUrl == "/employee/my-requests"));
    }

    [Fact]
    public async Task RequestCancellation_never_tells_an_approver_about_their_own_request()
    {
        // The HR department's own LineManager is not "HR staff" to the policy, so her leave
        // does need HR review — and she sits in the HR department, which puts her in her own
        // recipient list. She was being told, in the third person, that she had asked: it read
        // as somebody else's request to action. She gets the receipt and nothing else.
        var hrManager = NewHrUser("Elena HR", role: UserRole.LineManager);
        var colleague = NewHrUser("Carmen HR");
        var request = ApprovedRequest(hrManager);
        var context = CreateContext();

        await context.RequestCancellationAsync(hrManager.Id, request.Id, "Deadline moved.");

        await _notifications.DidNotReceive().CreateNotificationAsync(Arg.Is<Notification>(n =>
            n != null && n.UserId == hrManager.Id && n.Message.Contains("asked to cancel approved")));
        await _notifications.Received(1).CreateNotificationAsync(Arg.Is<Notification>(n =>
            n != null && n.UserId == hrManager.Id && n.Message.StartsWith("You asked HR")));

        // The rest of HR still gets the actionable one.
        await _notifications.Received(1).CreateNotificationAsync(Arg.Is<Notification>(n =>
            n != null && n.UserId == colleague.Id && n.Message.Contains("Elena HR asked to cancel")));
    }

    [Fact]
    public async Task RequestCancellation_still_saves_when_notifying_HR_fails()
    {
        var employee = NewUser("Ion Angajat");
        NewHrUser("Elena HR");
        var request = ApprovedRequest(employee);
        _notifications.CreateNotificationAsync(Arg.Any<Notification>())
            .Returns<Notification>(_ => throw new Exception("SMTP down"));
        var context = CreateContext();

        await context.RequestCancellationAsync(employee.Id, request.Id, "Deadline moved.");

        Assert.NotNull(request.CancellationRequestedAt);
        await _requests.Received(1).CancelRequestAsync(request);
    }

    // --- HR answering -----------------------------------------------------------------

    [Fact]
    public async Task HrApproval_cancels_the_leave_and_hands_the_days_back()
    {
        var employee = NewUser("Ion Angajat");
        var hr = NewHrUser("Elena HR");
        var request = RequestedForCancellation(employee);
        var context = CreateContext();

        await context.HrDecideCancellationAsync(hr.Id, request.Id, approve: true);

        // Cancelled is what the balance reads as "not spent".
        Assert.Equal(LeaveStatus.Cancelled, request.Status);
        await _requests.Received(1).CancelRequestAsync(request);
    }

    [Fact]
    public async Task Approving_an_in_progress_leave_shortens_it_instead_of_cancelling()
    {
        // The balance counts the working days of every Approved request, so cancelling leave
        // that is under way would hand back days the employee has already spent at home.
        // Shortening it to end today leaves those days spent and returns only the rest.
        var employee = NewUser("Ion Angajat");
        var hr = NewHrUser("Elena HR");
        var request = RequestedForCancellation(employee);
        request.StartDate = Today.AddDays(-3);
        request.EndDate = Today.AddDays(4);
        var context = CreateContext();

        await context.HrDecideCancellationAsync(hr.Id, request.Id, approve: true);

        Assert.Equal(LeaveStatus.Approved, request.Status);
        Assert.Equal(Today, request.EndDate);
        Assert.Equal(request.StartDate, Today.AddDays(-3));
        // Cleared, or HR keeps seeing it in their queue for ever.
        Assert.Null(request.CancellationRequestedAt);
        await _requests.Received(1).CancelRequestAsync(request);
    }

    [Fact]
    public async Task Approving_leave_that_has_not_started_still_cancels_it_outright()
    {
        // Nothing was consumed, so there is nothing to keep.
        var employee = NewUser("Ion Angajat");
        var hr = NewHrUser("Elena HR");
        var request = RequestedForCancellation(employee);
        request.StartDate = Today.AddDays(3);
        request.EndDate = Today.AddDays(6);
        var context = CreateContext();

        await context.HrDecideCancellationAsync(hr.Id, request.Id, approve: true);

        Assert.Equal(LeaveStatus.Cancelled, request.Status);
        Assert.Equal(Today.AddDays(6), request.EndDate);
    }

    [Fact]
    public async Task Leave_starting_today_keeps_today_as_taken()
    {
        // They were away today, so today counts — the shortening lands on today, not yesterday.
        var employee = NewUser("Ion Angajat");
        var hr = NewHrUser("Elena HR");
        var request = RequestedForCancellation(employee);
        request.StartDate = Today;
        request.EndDate = Today.AddDays(5);
        var context = CreateContext();

        await context.HrDecideCancellationAsync(hr.Id, request.Id, approve: true);

        Assert.Equal(LeaveStatus.Approved, request.Status);
        Assert.Equal(Today, request.EndDate);
    }

    [Fact]
    public async Task Cannot_ask_to_cancel_leave_with_no_days_left_to_return()
    {
        // Ends today: approving would shorten it to today and give back nothing, so there is
        // no point putting it in front of HR at all.
        var employee = NewUser("Ion Angajat");
        var request = ApprovedRequest(employee);
        request.StartDate = Today.AddDays(-2);
        request.EndDate = Today;
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.RequestCancellationAsync(employee.Id, request.Id, "Prea tarziu."));

        await _requests.DidNotReceiveWithAnyArgs().CancelRequestAsync(default!);
    }

    [Fact]
    public async Task HrDecline_leaves_the_leave_approved_and_clears_the_request()
    {
        var employee = NewUser("Ion Angajat");
        var hr = NewHrUser("Elena HR");
        var request = RequestedForCancellation(employee);
        var context = CreateContext();

        await context.HrDecideCancellationAsync(hr.Id, request.Id, approve: false);

        Assert.Equal(LeaveStatus.Approved, request.Status);
        // Cleared so the employee can ask again rather than being stuck.
        Assert.Null(request.CancellationRequestedAt);
        Assert.Null(request.CancellationReason);
    }

    [Fact]
    public async Task HrDecision_notifies_the_employee_either_way()
    {
        var employee = NewUser("Ion Angajat");
        var hr = NewHrUser("Elena HR");
        var request = RequestedForCancellation(employee);
        var context = CreateContext();

        await context.HrDecideCancellationAsync(hr.Id, request.Id, approve: true);

        await _notifications.Received(1).CreateNotificationAsync(Arg.Is<Notification>(n =>
            n != null && n.UserId == employee.Id && n.Message.Contains("approved")));
    }

    [Fact]
    public async Task HrDecision_refuses_a_caller_outside_the_HR_department()
    {
        // The page gates on the Department claim; a claim is not a control.
        var employee = NewUser("Ion Angajat");
        var impostor = NewUser("Ana Popescu");
        impostor.Department = Design;
        impostor.DepartmentId = Design.Id;
        var request = RequestedForCancellation(employee);
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.HrDecideCancellationAsync(impostor.Id, request.Id, approve: true));

        Assert.Equal(LeaveStatus.Approved, request.Status);
        await _requests.DidNotReceiveWithAnyArgs().CancelRequestAsync(default!);
    }

    [Fact]
    public async Task HrDecision_refuses_a_request_from_another_region()
    {
        // Acting stays regional even though looking is worldwide.
        var foreigner = NewUser("Ahmed Khan");
        foreigner.Region = Pakistan;
        foreigner.RegionId = Pakistan.Id;
        var hr = NewHrUser("Elena HR");
        var request = RequestedForCancellation(foreigner);
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.HrDecideCancellationAsync(hr.Id, request.Id, approve: true));

        await _requests.DidNotReceiveWithAnyArgs().CancelRequestAsync(default!);
    }

    [Fact]
    public async Task HrDecision_refuses_a_request_nobody_asked_to_cancel()
    {
        var employee = NewUser("Ion Angajat");
        var hr = NewHrUser("Elena HR");
        var request = ApprovedRequest(employee);
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.HrDecideCancellationAsync(hr.Id, request.Id, approve: true));

        await _requests.DidNotReceiveWithAnyArgs().CancelRequestAsync(default!);
    }

    [Fact]
    public async Task HrDecision_refuses_an_unknown_request()
    {
        var hr = NewHrUser("Elena HR");
        var context = CreateContext();

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            context.HrDecideCancellationAsync(hr.Id, Guid.NewGuid(), approve: true));
    }

    // --- delegation audit -------------------------------------------------------------

    [Fact]
    public async Task RequestCancellation_records_the_human_behind_a_borrowed_account()
    {
        var employee = NewUser("Ion Angajat");
        var (stand_in, delegation) = Delegation(employee);
        var request = ApprovedRequest(employee);
        var context = CreateContext();

        await context.RequestCancellationAsync(
            employee.Id, request.Id, "Deadline moved.",
            new ActingOnBehalf(stand_in.Id, delegation.Id));

        await _delegatedActions.Received(1).CreateAsync(Arg.Is<DelegatedAction>(action =>
            action != null
            && action.RealUserId == stand_in.Id
            && action.ActedAsUserId == employee.Id
            && action.ActionType == DelegatedActionType.LeaveCancellationRequested));
    }

    [Fact]
    public async Task HrDecision_records_the_human_behind_a_borrowed_account()
    {
        var employee = NewUser("Ion Angajat");
        var hr = NewHrUser("Elena HR");
        var (stand_in, delegation) = Delegation(hr);
        var request = RequestedForCancellation(employee);
        var context = CreateContext();

        await context.HrDecideCancellationAsync(
            hr.Id, request.Id, approve: false,
            new ActingOnBehalf(stand_in.Id, delegation.Id));

        await _delegatedActions.Received(1).CreateAsync(Arg.Is<DelegatedAction>(action =>
            action != null
            && action.RealUserId == stand_in.Id
            && action.ActedAsUserId == hr.Id
            && action.TargetUserId == employee.Id
            && action.ActionType == DelegatedActionType.LeaveCancellationRejected));
    }

    [Fact]
    public async Task Acting_as_yourself_writes_no_audit_row()
    {
        var employee = NewUser("Ion Angajat");
        var request = ApprovedRequest(employee);
        var context = CreateContext();

        await context.RequestCancellationAsync(employee.Id, request.Id, "Deadline moved.");

        await _delegatedActions.DidNotReceive().CreateAsync(Arg.Any<DelegatedAction>());
    }

    // --- fixture ----------------------------------------------------------------------

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private LeaveRequest ApprovedRequest(User owner)
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            User = owner,
            Type = LeaveType.Annual,
            StartDate = Today.AddDays(7),
            EndDate = Today.AddDays(9),
            Status = LeaveStatus.Approved,
            Approvals = new List<LeaveApproval>()
        };

        _requests.GetRequestByIdAsync(request.Id).Returns(request);
        return request;
    }

    private LeaveRequest RequestedForCancellation(User owner)
    {
        var request = ApprovedRequest(owner);
        request.CancellationRequestedAt = DateTime.UtcNow.AddHours(-2);
        request.CancellationReason = "Deadline moved.";
        return request;
    }

    private (User Delegate, ManagerDelegation Delegation) Delegation(User owner)
    {
        var stand_in = NewUser("Andrei Ionescu");
        var delegation = new ManagerDelegation
        {
            Id = Guid.NewGuid(),
            ManagerId = owner.Id,
            Manager = owner,
            DelegateId = stand_in.Id,
            Delegate = stand_in,
            StartDate = Today.AddDays(-1),
            EndDate = Today.AddDays(7),
            IsActive = true
        };

        _delegations.GetByIdAsync(delegation.Id).Returns(delegation);
        return (stand_in, delegation);
    }

    private User NewHrUser(string name, Region? region = null, UserRole role = UserRole.Employee)
    {
        var hr = NewUser(name, role);
        hr.Department = HrDepartment;
        hr.DepartmentId = HrDepartment.Id;
        if (region is not null)
        {
            hr.Region = region;
            hr.RegionId = region.Id;
        }
        return hr;
    }

    private User NewUser(string name, UserRole role = UserRole.Employee)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = $"{Guid.NewGuid():N}@siemens.com",
            Role = role,
            Status = UserStatus.Active,
            Region = Romania,
            RegionId = Romania.Id
        };

        _users.GetUserByIdAsync(user.Id).Returns(user);
        _everyone.Add(user);
        return user;
    }

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
}
