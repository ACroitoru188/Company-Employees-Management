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

public class ContractContextTests
{
    private readonly IContractGateway _contracts = Substitute.For<IContractGateway>();
    private readonly IUserGateway _users = Substitute.For<IUserGateway>();
    private readonly IManagerDelegationGateway _delegations = Substitute.For<IManagerDelegationGateway>();
    private readonly INotificationGateway _notifications = Substitute.For<INotificationGateway>();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();
    private readonly IImpersonationGateway _sessions = Substitute.For<IImpersonationGateway>();
    private readonly IDelegatedActionGateway _delegatedActions = Substitute.For<IDelegatedActionGateway>();

    [Fact]
    public async Task ExtendContractAsync_throws_when_contract_not_found()
    {
        var manager = NewUser(UserRole.LineManager, Guid.NewGuid());
        _users.GetUserByIdAsync(manager.Id).Returns(manager);
        _contracts.GetByIdAsync(Arg.Any<Guid>()).Returns((Contract?)null);

        var context = CreateContext();

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            context.ExtendContractAsync(manager.Id, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today.AddMonths(6))));
    }

    [Fact]
    public async Task ExtendContractAsync_throws_when_region_differs()
    {
        var manager = NewUser(UserRole.LineManager, Guid.NewGuid());
        var employee = NewUser(UserRole.Employee, Guid.NewGuid());
        var contract = new Contract { Id = Guid.NewGuid(), UserId = employee.Id, User = employee };

        _users.GetUserByIdAsync(manager.Id).Returns(manager);
        _contracts.GetByIdAsync(contract.Id).Returns(contract);

        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.ExtendContractAsync(manager.Id, contract.Id, DateOnly.FromDateTime(DateTime.Today.AddMonths(6))));
    }

    [Fact]
    public async Task ExtendContractAsync_throws_when_not_direct_manager_or_delegate()
    {
        var regionId = Guid.NewGuid();
        var manager = NewUser(UserRole.LineManager, regionId);
        var otherManager = NewUser(UserRole.LineManager, regionId);
        var employee = NewUser(UserRole.Employee, regionId);
        employee.ManagerId = otherManager.Id;
        var contract = new Contract { Id = Guid.NewGuid(), UserId = employee.Id, User = employee };

        _users.GetUserByIdAsync(manager.Id).Returns(manager);
        _contracts.GetByIdAsync(contract.Id).Returns(contract);
        _delegations.GetDelegatedManagerIdsAsync(manager.Id, Arg.Any<DateOnly>()).Returns([]);

        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.ExtendContractAsync(manager.Id, contract.Id, DateOnly.FromDateTime(DateTime.Today.AddMonths(6))));
    }

    [Fact]
    public async Task ExtendContractAsync_extends_successfully_when_valid()
    {
        var regionId = Guid.NewGuid();
        var manager = NewUser(UserRole.LineManager, regionId);
        var employee = NewUser(UserRole.Employee, regionId);
        employee.ManagerId = manager.Id;

        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            User = employee,
            Type = ContractType.Determinate,
            Status = ContractStatus.Active,
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(1))
        };

        _users.GetUserByIdAsync(manager.Id).Returns(manager);
        _contracts.GetByIdAsync(contract.Id).Returns(contract);

        var newEndDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(6));
        var context = CreateContext();

        await context.ExtendContractAsync(manager.Id, contract.Id, newEndDate);

        Assert.Equal(newEndDate, contract.EndDate);
        await _contracts.Received(1).UpdateAsync(contract);
        await _notifications.Received(1).CreateNotificationAsync(Arg.Is<Notification>(n =>
            n != null && n.UserId == employee.Id && n.Message.Contains("extended")));
    }

    [Fact]
    public async Task TerminateContractAsync_terminates_active_contract()
    {
        var regionId = Guid.NewGuid();
        var manager = NewUser(UserRole.LineManager, regionId);
        var employee = NewUser(UserRole.Employee, regionId);
        employee.ManagerId = manager.Id;

        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            User = employee,
            Type = ContractType.Indeterminate,
            Status = ContractStatus.Active,
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddYears(-1))
        };

        _users.GetUserByIdAsync(manager.Id).Returns(manager);
        _contracts.GetByIdAsync(contract.Id).Returns(contract);

        var context = CreateContext();

        await context.TerminateContractAsync(manager.Id, contract.Id, "Performance");

        Assert.Equal(ContractStatus.Terminated, contract.Status);
        Assert.Contains("Performance", contract.Notes);
        await _contracts.Received(1).UpdateAsync(contract);
        await _notifications.Received(1).CreateNotificationAsync(Arg.Is<Notification>(n =>
            n != null && n.UserId == employee.Id && n.Message.Contains("terminated")));
    }

    private ContractContext CreateContext()
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

    private static User NewUser(UserRole role, Guid regionId) => new()
    {
        Id = Guid.NewGuid(),
        Name = role.ToString(),
        Role = role,
        RegionId = regionId
    };
}
