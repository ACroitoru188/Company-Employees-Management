using CompanyEmployees.Data.Entities;
using CompanyEmployees.Data.Services;
using Xunit;

namespace CompanyEmployees.Data.UnitTests;

public class PermissionServiceTests
{
    [Fact]
    public void HasPermission_EmployeeHasPermission_ReturnsTrue()
    {
        var role = new Role 
        { 
            Name = "Tester",
            Color = "#000000",
            Permissions = Permission.ViewEmployees 
        };
        
        var employee = new Employee();
        employee.Roles.Add(role);

        var result = PermissionService.HasPermission(employee, Permission.ViewEmployees);

        Assert.True(result);
    }
}
