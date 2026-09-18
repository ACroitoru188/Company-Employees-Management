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

public class DelegationContextTests
{
    private readonly IManagerDelegationGateway _delegations = Substitute.For<IManagerDelegationGateway>();
    private readonly IUserGateway _users = Substitute.For<IUserGateway>();
    private readonly IDelegatedActionGateway _delegatedActions = Substitute.For<IDelegatedActionGateway>();
    private readonly INotificationGateway _notifications = Substitute.For<INotificationGateway>();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    [Fact]
    public async Task CreateDelegationAsync_rejects_borrowed_account_delegation()
    {
        var context = CreateContext();
        var delegatorId = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        var end = DateOnly.FromDateTime(DateTime.Today.AddDays(5));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.CreateDelegationAsync(delegatorId, delegateId, start, end, "Leave", new ActingOnBehalf(Guid.NewGuid(), Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateDelegationAsync_rejects_self_delegation()
    {
        var context = CreateContext();
        var id = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        var end = DateOnly.FromDateTime(DateTime.Today.AddDays(5));

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.CreateDelegationAsync(id, id, start, end, "Leave"));
    }

    [Fact]
    public async Task CreateDelegationAsync_rejects_invalid_date_range()
    {
        var context = CreateContext();
        var delegatorId = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(5));
        var end = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.CreateDelegationAsync(delegatorId, delegateId, start, end, "Leave"));
    }

    [Fact]
    public async Task CreateDelegationAsync_rejects_past_period()
    {
        var context = CreateContext();
        var delegatorId = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-10));
        var end = DateOnly.FromDateTime(DateTime.Today.AddDays(-5));

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.CreateDelegationAsync(delegatorId, delegateId, start, end, "Leave"));
    }

    [Fact]
    public async Task CreateDelegationAsync_rejects_a_period_overlapping_an_existing_delegation()
    {
        var regionId = Guid.NewGuid();
        var manager = NewUser(UserRole.LineManager, regionId);
        var delegateUser = NewUser(UserRole.Employee, regionId);
        delegateUser.Status = UserStatus.Active;
        _users.GetUserByIdAsync(manager.Id).Returns(manager);
        _users.GetUserByIdAsync(delegateUser.Id).Returns(delegateUser);

        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(5));
        var end = DateOnly.FromDateTime(DateTime.Today.AddDays(10));
        _delegations.HasActiveDelegationInPeriodAsync(manager.Id, start, end).Returns(true);
        var context = CreateContext();

        var exception = await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.CreateDelegationAsync(manager.Id, delegateUser.Id, start, end, "Leave"));

        Assert.Contains("already have a delegation", exception.Message);
        await _delegations.DidNotReceiveWithAnyArgs().CreateAsync(default!);
    }

    [Fact]
    public async Task CreateDelegationAsync_allows_a_period_that_does_not_overlap()
    {
        var regionId = Guid.NewGuid();
        var manager = NewUser(UserRole.LineManager, regionId);
        var delegateUser = NewUser(UserRole.Employee, regionId);
        delegateUser.Status = UserStatus.Active;
        _users.GetUserByIdAsync(manager.Id).Returns(manager);
        _users.GetUserByIdAsync(delegateUser.Id).Returns(delegateUser);

        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(5));
        var end = DateOnly.FromDateTime(DateTime.Today.AddDays(10));
        _delegations.HasActiveDelegationInPeriodAsync(manager.Id, start, end).Returns(false);
        var context = CreateContext();

        var result = await context.CreateDelegationAsync(manager.Id, delegateUser.Id, start, end, "Leave");

        Assert.Equal(delegateUser.Id, result.DelegateId);
        await _delegations.Received(1).CreateAsync(Arg.Any<ManagerDelegation>());
    }

    [Fact]
    public async Task CancelDelegationAsync_cancels_active_delegation()
    {
        var delegatorId = Guid.NewGuid();
        var delegation = new ManagerDelegation
        {
            Id = Guid.NewGuid(),
            ManagerId = delegatorId,
            DelegateId = Guid.NewGuid(),
            IsActive = true
        };
        _delegations.GetByIdAsync(delegation.Id).Returns(delegation);
        var context = CreateContext();

        await context.CancelDelegationAsync(delegatorId, delegation.Id);

        Assert.False(delegation.IsActive);
        await _delegations.Received(1).UpdateAsync(delegation);
    }

    [Fact]
    public async Task CancelDelegationAsync_throws_unauthorized_if_caller_is_not_delegator()
    {
        var delegatorId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var delegation = new ManagerDelegation
        {
            Id = Guid.NewGuid(),
            ManagerId = delegatorId,
            DelegateId = Guid.NewGuid(),
            IsActive = true
        };
        _delegations.GetByIdAsync(delegation.Id).Returns(delegation);
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.CancelDelegationAsync(otherUserId, delegation.Id));
    }

    [Fact]
    public async Task GetEligibleDelegatesAsync_returns_active_colleagues_in_same_region_except_self()
    {
        var region1 = Guid.NewGuid();
        var region2 = Guid.NewGuid();
        var caller = NewUser(UserRole.Employee, region1);
        var colleague1 = NewUser(UserRole.Employee, region1);
        colleague1.Status = UserStatus.Active;
        var colleague2 = NewUser(UserRole.LineManager, region1);
        colleague2.Status = UserStatus.Inactive;
        var colleague3 = NewUser(UserRole.Employee, region2);
        colleague3.Status = UserStatus.Active;

        _users.GetUserByIdAsync(caller.Id).Returns(caller);
        _users.GetAllUsersAsync().Returns([caller, colleague1, colleague2, colleague3]);
        var context = CreateContext();

        var result = await context.GetEligibleDelegatesAsync(caller.Id);

        Assert.Single(result);
        Assert.Equal(colleague1.Id, result[0].Id);
    }

    private DelegationContext CreateContext()
    {
        var notificationContext = new NotificationContext(_notifications, _dispatcher);
        return new DelegationContext(
            NullLogger<DelegationContext>.Instance,
            _delegations,
            _users,
            _delegatedActions,
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
