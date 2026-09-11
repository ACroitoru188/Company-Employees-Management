using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyEmployees.Integration.Tests.Gateway;

public abstract class UserRepositoryTestsBase : IntegrationTestBase
{
    protected UserRepositoryTestsBase(IDatabaseFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task CreateUserAsync_And_GetUserByIdAsync_ReturnsUserWithRelationships()
    {
        // Arrange
        var userRepo = Services.GetRequiredService<IUserGateway>();

        var region = new Region
        {
            Id = Guid.NewGuid(),
            Name = "Transylvania",
            Code = "TR",
            IsActive = true
        };
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Engineering"
        };

        Db.Regions.Add(region);
        Db.Departments.Add(department);
        await Db.SaveChangesAsync();

        var managerId = Guid.NewGuid();
        var manager = new User
        {
            Id = managerId,
            UserName = "manager@example.com",
            Email = "manager@example.com",
            Name = "Alice Manager",
            Role = UserRole.LineManager,
            RegionId = region.Id,
            DepartmentId = department.Id,
            Status = UserStatus.Active
        };
        await userRepo.CreateUserAsync(manager);

        var employeeId = Guid.NewGuid();
        var employee = new User
        {
            Id = employeeId,
            UserName = "employee@example.com",
            Email = "employee@example.com",
            Name = "Bob Developer",
            Role = UserRole.Employee,
            RegionId = region.Id,
            DepartmentId = department.Id,
            ManagerId = managerId,
            Status = UserStatus.Active
        };
        await userRepo.CreateUserAsync(employee);

        // Act
        var result = await userRepo.GetUserByIdAsync(employeeId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Bob Developer", result.Name);
        Assert.NotNull(result.Department);
        Assert.Equal("Engineering", result.Department.Name);
        Assert.NotNull(result.Region);
        Assert.Equal("Transylvania", result.Region.Name);
        Assert.NotNull(result.Manager);
        Assert.Equal("Alice Manager", result.Manager.Name);
    }

    [Fact]
    public async Task GetDirectReportsAsync_ReturnsOnlyActiveReportsForManager()
    {
        // Arrange
        var userRepo = Services.GetRequiredService<IUserGateway>();

        var region = new Region
        {
            Id = Guid.NewGuid(),
            Name = "Muntenia",
            Code = "MU",
            IsActive = true
        };
        Db.Regions.Add(region);
        await Db.SaveChangesAsync();

        var manager = new User
        {
            Id = Guid.NewGuid(),
            UserName = "lead@example.com",
            Email = "lead@example.com",
            Name = "Team Lead",
            Role = UserRole.LineManager,
            RegionId = region.Id,
            Status = UserStatus.Active
        };
        await userRepo.CreateUserAsync(manager);

        var activeReport1 = new User
        {
            Id = Guid.NewGuid(),
            UserName = "charlie@example.com",
            Email = "charlie@example.com",
            Name = "Charlie Developer",
            Role = UserRole.Employee,
            RegionId = region.Id,
            ManagerId = manager.Id,
            Status = UserStatus.Active
        };
        var activeReport2 = new User
        {
            Id = Guid.NewGuid(),
            UserName = "alex@example.com",
            Email = "alex@example.com",
            Name = "Alex Developer",
            Role = UserRole.Employee,
            RegionId = region.Id,
            ManagerId = manager.Id,
            Status = UserStatus.Active
        };
        var inactiveReport = new User
        {
            Id = Guid.NewGuid(),
            UserName = "inactive@example.com",
            Email = "inactive@example.com",
            Name = "Inactive Dev",
            Role = UserRole.Employee,
            RegionId = region.Id,
            ManagerId = manager.Id,
            Status = UserStatus.Inactive
        };

        await userRepo.CreateUserAsync(activeReport1);
        await userRepo.CreateUserAsync(activeReport2);
        await userRepo.CreateUserAsync(inactiveReport);

        // Act
        var directReports = await userRepo.GetDirectReportsAsync(manager.Id);

        // Assert
        Assert.Equal(2, directReports.Count);
        Assert.Equal("Alex Developer", directReports[0].Name); // Sorted by Name
        Assert.Equal("Charlie Developer", directReports[1].Name);
        Assert.DoesNotContain(directReports, u => u.Id == inactiveReport.Id);
    }

    [Fact]
    public async Task UpdateUserAsync_UpdatesStatusAndPersists()
    {
        // Arrange
        var userRepo = Services.GetRequiredService<IUserGateway>();

        var region = new Region
        {
            Id = Guid.NewGuid(),
            Name = "Moldova",
            Code = "MD",
            IsActive = true
        };
        Db.Regions.Add(region);
        await Db.SaveChangesAsync();

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "to_deactivate@example.com",
            Email = "to_deactivate@example.com",
            Name = "Leaving Employee",
            Role = UserRole.Employee,
            RegionId = region.Id,
            Status = UserStatus.Active
        };
        await userRepo.CreateUserAsync(user);

        // Act
        user.Status = UserStatus.Inactive;
        await userRepo.UpdateUserAsync(user);

        // Assert
        var updated = await Db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(updated);
        Assert.Equal(UserStatus.Inactive, updated.Status);
    }
}
