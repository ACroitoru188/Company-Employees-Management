using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainInvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Tests;

// The working-day guards live in BaseContext, and which exception *type* they throw is not a
// detail: ManagerDashboard and HRDashboard both catch the domain type by name to turn a refusal
// into a message inside the edit dialog. When these guards moved out of EmployeeContext the
// alias stayed behind, so they threw System's type instead, escaped both catches, and took the
// Blazor circuit down — the page froze rather than explaining itself.
//
// So these assert the type, not just that something was thrown. Assert.ThrowsAsync is exact
// rather than assignable, which is exactly the distinction that was lost.
public class LeaveContextWorkingDayTests
{
    private readonly ILeaveRequestGateway _requests = Substitute.For<ILeaveRequestGateway>();
    private readonly IUserGateway _users = Substitute.For<IUserGateway>();
    private readonly IContractGateway _contracts = Substitute.For<IContractGateway>();
    private readonly IManagerDelegationGateway _delegations = Substitute.For<IManagerDelegationGateway>();
    private readonly IPublicHolidayProvider _holidays = Substitute.For<IPublicHolidayProvider>();
    private readonly INotificationGateway _notifications = Substitute.For<INotificationGateway>();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();
    private readonly IImpersonationGateway _sessions = Substitute.For<IImpersonationGateway>();
    private readonly IDelegatedActionGateway _delegatedActions = Substitute.For<IDelegatedActionGateway>();

    private static readonly Region Romania = new() { Id = Guid.NewGuid(), Name = "Romania", Code = "RO" };

    // Sat 2026-09-19, Mon 2026-09-21, Sat 2026-09-26. LaterSaturday exists because an end date
    // *before* the start hits "End date must not be before start date" first — a guard thrown
    // from LeaveContext, which still has the alias — so the test would pass without ever
    // reaching the working-day check it claims to exercise.
    private static readonly DateOnly Saturday = new(2026, 9, 19);
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly LaterSaturday = new(2026, 9, 26);

    public LeaveContextWorkingDayTests() =>
        _holidays.GetHolidaysAsync(Arg.Any<string>(), Arg.Any<int>())
            .Returns(Array.Empty<PublicHoliday>());

    [Fact]
    public async Task Moving_a_request_onto_a_weekend_throws_the_domain_exception()
    {
        // The dashboards catch this exact type. Anything else escapes them and kills the page.
        var request = PendingRequest();
        var context = CreateContext();

        var error = await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.UpdateRequestDatesAsync(request.Id, Saturday, Monday));

        Assert.Contains("working day", error.Message);
    }

    [Fact]
    public async Task An_end_date_on_a_weekend_is_refused_the_same_way()
    {
        var request = PendingRequest();
        var context = CreateContext();

        var error = await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.UpdateRequestDatesAsync(request.Id, Monday, LaterSaturday));

        Assert.Contains("working day", error.Message);
    }

    [Fact]
    public async Task A_public_holiday_is_refused_with_the_domain_exception_too()
    {
        // Same guard, second branch: the holiday lookup rather than the weekend check.
        _holidays.GetHolidaysAsync("RO", Monday.Year)
            .Returns(new[] { new PublicHoliday(Monday, "Ziua Recoltei") });

        var request = PendingRequest();
        var context = CreateContext();

        var error = await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.UpdateRequestDatesAsync(request.Id, Monday, Monday));

        Assert.Contains("Ziua Recoltei", error.Message);
    }

    private LeaveRequest PendingRequest()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Ion Angajat",
            Email = "ion@siemens.com",
            Role = UserRole.Employee,
            Status = UserStatus.Active,
            Region = Romania,
            RegionId = Romania.Id
        };

        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            Type = LeaveType.Annual,
            StartDate = Monday,
            EndDate = Monday,
            Status = LeaveStatus.Pending,
            Approvals = new List<LeaveApproval>()
        };

        _requests.GetRequestByIdAsync(request.Id).Returns(request);
        _requests.GetRequestsByUserAsync(user.Id).Returns(new List<LeaveRequest> { request });
        _users.GetUserByIdAsync(user.Id).Returns(user);
        return request;
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
