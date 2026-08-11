using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainInvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Tests;

public class ManagerContextTests
{
    private readonly ILeaveRequestGateway _requests = Substitute.For<ILeaveRequestGateway>();
    private readonly IUserGateway _users = Substitute.For<IUserGateway>();
    private readonly IContractGateway _contracts = Substitute.For<IContractGateway>();
    private readonly IManagerDelegationGateway _delegations = Substitute.For<IManagerDelegationGateway>();
    private readonly INotificationGateway _notifications = Substitute.For<INotificationGateway>();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    [Fact]
    public async Task DecideRequestAsync_keeps_request_pending_after_manager_approval_when_hr_is_required()
    {
        var setup = ArrangeDirectManagerRequest();
        var context = CreateContext();

        var result = await context.DecideRequestAsync(setup.Manager.Id, setup.Request.Id, approve: true);

        Assert.Equal(LeaveStatus.Pending, result.Status);
        var approval = Assert.Single(result.Approvals);
        Assert.Equal(LeaveApproval.ManagerApprovalStep, approval.Step);
        Assert.Equal(LeaveStatus.Approved, approval.Status);
        await _requests.Received(1).SaveDecisionAsync(setup.Request, approval);
        await _notifications.Received(1).CreateNotificationAsync(
            Arg.Is<Notification>(n =>
                n != null &&
                n.UserId == setup.Employee.Id &&
                n.Message != null &&
                n.Message.Contains("awaiting HR approval")));
    }

    [Fact]
    public async Task DecideRequestAsync_rejection_immediately_rejects_request()
    {
        var setup = ArrangeDirectManagerRequest();
        var context = CreateContext();

        var result = await context.DecideRequestAsync(setup.Manager.Id, setup.Request.Id, approve: false);

        Assert.Equal(LeaveStatus.Rejected, result.Status);
        var approval = Assert.Single(result.Approvals);
        Assert.Equal(LeaveStatus.Rejected, approval.Status);
        await _requests.Received(1).SaveDecisionAsync(setup.Request, approval);
    }

    [Fact]
    public async Task DecideRequestAsync_rejects_cross_region_review()
    {
        var setup = ArrangeDirectManagerRequest();
        setup.Employee.RegionId = Guid.NewGuid();
        var context = CreateContext();

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.DecideRequestAsync(setup.Manager.Id, setup.Request.Id, approve: true));

        Assert.Contains("another region", exception.Message);
        await _requests.DidNotReceiveWithAnyArgs()
            .SaveDecisionAsync(default!, default!);
    }

    [Fact]
    public async Task DecideRequestAsync_rejects_a_second_manager_decision()
    {
        var setup = ArrangeDirectManagerRequest();
        setup.Request.Approvals.Add(new LeaveApproval
        {
            Step = LeaveApproval.ManagerApprovalStep,
            Status = LeaveStatus.Approved
        });
        var context = CreateContext();

        var exception = await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.DecideRequestAsync(setup.Manager.Id, setup.Request.Id, approve: true));

        Assert.Contains("already decided", exception.Message);
        await _requests.DidNotReceiveWithAnyArgs()
            .SaveDecisionAsync(default!, default!);
    }

    [Fact]
    public async Task DecideRequestAsync_allows_an_active_delegate_to_approve()
    {
        var setup = ArrangeDirectManagerRequest();
        var delegateManager = NewUser(UserRole.LineManager, setup.Manager.RegionId);
        _users.GetUserByIdAsync(delegateManager.Id).Returns(delegateManager);
        _delegations.GetDelegatedManagerIdsAsync(delegateManager.Id, Arg.Any<DateOnly>())
            .Returns([setup.Manager.Id]);
        var context = CreateContext();

        var result = await context.DecideRequestAsync(delegateManager.Id, setup.Request.Id, approve: true);

        var approval = Assert.Single(result.Approvals);
        Assert.Equal(delegateManager.Id, approval.ApproverId);
        await _requests.Received(1).SaveDecisionAsync(setup.Request, approval);
    }

    [Fact]
    public async Task DecideRequestAsync_keeps_saved_decision_when_notification_fails()
    {
        var setup = ArrangeDirectManagerRequest();
        _notifications.CreateNotificationAsync(Arg.Any<Notification>())
            .Returns<Task<Notification>>(_ => throw new Exception("Notification store unavailable"));
        var context = CreateContext();

        var result = await context.DecideRequestAsync(setup.Manager.Id, setup.Request.Id, approve: true);

        Assert.Single(result.Approvals);
        await _requests.Received(1)
            .SaveDecisionAsync(setup.Request, Arg.Any<LeaveApproval>());
    }

    private (User Manager, User Employee, LeaveRequest Request) ArrangeDirectManagerRequest()
    {
        var regionId = Guid.NewGuid();
        var manager = NewUser(UserRole.LineManager, regionId);
        var employee = NewUser(UserRole.Employee, regionId);
        employee.ManagerId = manager.Id;
        employee.Manager = manager;

        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            User = employee,
            Type = LeaveType.Annual,
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(12)),
            Status = LeaveStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        _users.GetUserByIdAsync(manager.Id).Returns(manager);
        _requests.GetRequestByIdAsync(request.Id).Returns(request);
        _delegations.GetDelegatedManagerIdsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>())
            .Returns([]);

        return (manager, employee, request);
    }

    private ManagerContext CreateContext()
    {
        var notificationContext = new NotificationContext(_notifications, _dispatcher);
        return new ManagerContext(
            NullLogger<ManagerContext>.Instance,
            _requests,
            _users,
            _contracts,
            _delegations,
            notificationContext);
    }

    private static User NewUser(UserRole role, Guid regionId) => new()
    {
        Id = Guid.NewGuid(),
        Name = role.ToString(),
        Role = role,
        RegionId = regionId
    };
}
