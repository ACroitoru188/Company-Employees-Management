using CompanyEmployees.Domain;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyEmployees.Integration.Tests.Gateway;

public abstract class LeaveRequestRepositoryTestsBase : IntegrationTestBase
{
    protected LeaveRequestRepositoryTestsBase(IDatabaseFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task EnsureDefaultAllocationsAsync_CreatesAllDefaultLeaveTypes()
    {
        // Arrange
        var leaveRepo = Services.GetRequiredService<ILeaveRequestGateway>();

        var region = new Region
        {
            Id = Guid.NewGuid(),
            Name = "Banat",
            Code = "BA",
            IsActive = true
        };
        Db.Regions.Add(region);
        await Db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "allocated@example.com",
            Email = "allocated@example.com",
            Name = "Allocated Employee",
            Role = UserRole.Employee,
            RegionId = region.Id,
            Status = UserStatus.Active
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        int currentYear = DateTime.UtcNow.Year;

        // Act
        await leaveRepo.EnsureDefaultAllocationsAsync(userId, currentYear);

        // Assert
        var allocations = await leaveRepo.GetAllocationsByUserAsync(userId, currentYear);
        var expectedTypes = Enum.GetValues<LeaveType>();

        Assert.Equal(expectedTypes.Length, allocations.Count);
        foreach (var type in expectedTypes)
        {
            var match = allocations.FirstOrDefault(a => a.LeaveType == type);
            Assert.NotNull(match);
            Assert.Equal(LeaveAllocationPolicy.DefaultDays(type), match.NumberOfDays);
        }
    }

    [Fact]
    public async Task CreateRequestAsync_And_GetRequestsByUserAsync_ReturnsRequestWithApprovals()
    {
        // Arrange
        var leaveRepo = Services.GetRequiredService<ILeaveRequestGateway>();

        var region = new Region
        {
            Id = Guid.NewGuid(),
            Name = "Dobrogea",
            Code = "DO",
            IsActive = true
        };
        Db.Regions.Add(region);
        await Db.SaveChangesAsync();

        var manager = new User
        {
            Id = Guid.NewGuid(),
            UserName = "mgr@example.com",
            Email = "mgr@example.com",
            Name = "Manager Approver",
            Role = UserRole.LineManager,
            RegionId = region.Id,
            Status = UserStatus.Active
        };
        var requester = new User
        {
            Id = Guid.NewGuid(),
            UserName = "req@example.com",
            Email = "req@example.com",
            Name = "Requester",
            Role = UserRole.Employee,
            RegionId = region.Id,
            Status = UserStatus.Active
        };
        Db.Users.AddRange(manager, requester);
        await Db.SaveChangesAsync();

        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            UserId = requester.Id,
            Type = LeaveType.Annual,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Status = LeaveStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await leaveRepo.CreateRequestAsync(request);

        // Add an approval entry
        var approval = new LeaveApproval
        {
            Id = Guid.NewGuid(),
            LeaveRequestId = request.Id,
            ApproverId = manager.Id,
            Step = LeaveApproval.ManagerApprovalStep,
            Status = LeaveStatus.Approved,
            ReviewedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        request.Status = LeaveStatus.Approved;
        await leaveRepo.SaveDecisionAsync(request, approval);

        // Act
        var userRequests = await leaveRepo.GetRequestsByUserAsync(requester.Id);

        // Assert
        var fetchedRequest = Assert.Single(userRequests);
        Assert.Equal(request.Id, fetchedRequest.Id);
        Assert.Equal(LeaveStatus.Approved, fetchedRequest.Status);
        var fetchedApproval = Assert.Single(fetchedRequest.Approvals);
        Assert.Equal(manager.Id, fetchedApproval.ApproverId);
        Assert.NotNull(fetchedApproval.Approver);
        Assert.Equal("Manager Approver", fetchedApproval.Approver.Name);
    }
}
