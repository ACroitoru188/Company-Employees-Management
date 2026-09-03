using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using DomainExceptions = CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CompanyEmployees.Application.Tests;

public class EmployeeContextRegionTests
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

    private static readonly Region Romania = new() { Id = Guid.NewGuid(), Name = "Romania", Code = "RO", IsActive = true };
    private static readonly Region Pakistan = new() { Id = Guid.NewGuid(), Name = "Pakistan", Code = "PK", IsActive = true };

    [Fact]
    public async Task AssignUserToRegionAsync_updates_user_region_and_severs_cross_region_reporting()
    {
        var admin = new User { Id = Guid.NewGuid(), Role = UserRole.Admin, RegionId = Romania.Id, Region = Romania };
        var manager = new User { Id = Guid.NewGuid(), Role = UserRole.LineManager, RegionId = Romania.Id, Region = Romania };
        var employee = new User { Id = Guid.NewGuid(), Role = UserRole.Employee, RegionId = Romania.Id, Region = Romania, ManagerId = manager.Id, Manager = manager };

        _regions.GetByIdAsync(Pakistan.Id).Returns(Pakistan);
        _users.GetUserByIdAsync(admin.Id).Returns(admin);
        _users.GetUserByIdAsync(employee.Id).Returns(employee);
        _users.GetUserByIdAsync(manager.Id).Returns(manager);
        _users.GetAllDirectReportsAsync(employee.Id).Returns(new List<User>());

        var context = CreateContext();

        await context.AssignUserToRegionAsync(admin.Id, employee.Id, Pakistan.Id);

        Assert.Equal(Pakistan.Id, employee.RegionId);
        Assert.Equal(Pakistan, employee.Region);
        Assert.Null(employee.ManagerId);
        Assert.Null(employee.Manager);
        await _users.Received(1).UpdateUserAsync(employee);
    }

    [Fact]
    public async Task AssignUserToRegionAsync_rejects_inactive_region()
    {
        var inactiveRegion = new Region { Id = Guid.NewGuid(), Name = "Old Region", Code = "OR", IsActive = false };
        _regions.GetByIdAsync(inactiveRegion.Id).Returns(inactiveRegion);

        var context = CreateContext();

        await Assert.ThrowsAsync<DomainExceptions.InvalidOperationException>(() =>
            context.AssignUserToRegionAsync(Guid.NewGuid(), Guid.NewGuid(), inactiveRegion.Id));
    }

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
