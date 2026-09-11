using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyEmployees.Integration.Tests.Application;

public abstract class LeaveWorkflowTestsBase : IntegrationTestBase
{
    protected LeaveWorkflowTestsBase(IDatabaseFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task FullLeaveWorkflow_Submit_ManagerApprove_HrApprove_FinalizesInDbAndNotifies()
    {
        // 1. Setup Master Data (Region, Department, Manager, HR Admin, Employee, Contract)
        var region = new Region
        {
            Id = Guid.NewGuid(),
            Name = "Romania",
            Code = "RO",
            IsActive = true
        };
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = "R&D"
        };
        Db.Regions.Add(region);
        Db.Departments.Add(department);
        await Db.SaveChangesAsync();

        var managerId = Guid.NewGuid();
        var manager = new User
        {
            Id = managerId,
            UserName = "linemanager@siemens.com",
            Email = "linemanager@siemens.com",
            Name = "Mihai Manager",
            Role = UserRole.LineManager,
            RegionId = region.Id,
            DepartmentId = department.Id,
            Status = UserStatus.Active
        };

        var hrUserId = Guid.NewGuid();
        var hrUser = new User
        {
            Id = hrUserId,
            UserName = "hradmin@siemens.com",
            Email = "hradmin@siemens.com",
            Name = "Helen HR",
            Role = UserRole.Admin,
            RegionId = region.Id,
            DepartmentId = department.Id,
            Status = UserStatus.Active
        };

        var employeeId = Guid.NewGuid();
        var employee = new User
        {
            Id = employeeId,
            UserName = "developer@siemens.com",
            Email = "developer@siemens.com",
            Name = "Dan Developer",
            Role = UserRole.Employee,
            RegionId = region.Id,
            DepartmentId = department.Id,
            ManagerId = managerId,
            Status = UserStatus.Active
        };

        Db.Users.AddRange(manager, hrUser, employee);
        await Db.SaveChangesAsync();

        // Add an active contract for employee so annual entitlement calculates
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = employeeId,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            Type = ContractType.Indeterminate,
            Status = ContractStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        Db.Contracts.Add(contract);
        await Db.SaveChangesAsync();

        // 2. Submit Leave Request via EmployeeContext
        var employeeContext = Services.GetRequiredService<EmployeeContext>();

        // Find next Monday and Wednesday to ensure working days
        var nextMonday = DateOnly.FromDateTime(DateTime.Today.AddDays(7));
        while (nextMonday.DayOfWeek != DayOfWeek.Monday)
        {
            nextMonday = nextMonday.AddDays(1);
        }
        var nextWednesday = nextMonday.AddDays(2);

        var submittedRequest = await employeeContext.SubmitRequestAsync(
            employeeId,
            LeaveType.Annual,
            nextMonday,
            nextWednesday,
            "Family vacation");

        Assert.NotNull(submittedRequest);
        Assert.Equal(LeaveStatus.Pending, submittedRequest.Status);
        Assert.Equal(employeeId, submittedRequest.UserId);

        // Verify persisted to database
        var dbRequest = await Db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == submittedRequest.Id);
        Assert.NotNull(dbRequest);
        Assert.Equal(LeaveStatus.Pending, dbRequest.Status);

        // 3. Check Manager's Pending Requests via ManagerContext
        var managerContext = Services.GetRequiredService<ManagerContext>();
        var pendingRequests = await managerContext.GetPendingRequestsForManagerAsync(managerId);

        var foundRequest = Assert.Single(pendingRequests);
        Assert.Equal(submittedRequest.Id, foundRequest.Id);
        Assert.Equal("Dan Developer", foundRequest.User.Name);

        // 4. Line Manager approves the request (step 1)
        var managerApprovedResult = await managerContext.DecideRequestAsync(
            managerId,
            submittedRequest.Id,
            approve: true);

        // Under policy, an employee with a LineManager requires both Manager AND HR approval.
        // Therefore, after manager approval the request remains Pending awaiting HR.
        Assert.Equal(LeaveStatus.Pending, managerApprovedResult.Status);

        // 5. HR approves the request (step 2)
        var hrApprovedResult = await employeeContext.HrDecideRequestAsync(
            hrUserId,
            submittedRequest.Id,
            approve: true);

        // Now fully approved
        Assert.Equal(LeaveStatus.Approved, hrApprovedResult.Status);

        // 6. Verify final state in real database
        var finalDbRequest = await Db.LeaveRequests
            .Include(r => r.Approvals)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == submittedRequest.Id);

        Assert.NotNull(finalDbRequest);
        Assert.Equal(LeaveStatus.Approved, finalDbRequest.Status);
        Assert.Equal(2, finalDbRequest.Approvals.Count);

        var managerApproval = finalDbRequest.Approvals.FirstOrDefault(a => a.Step == LeaveApproval.ManagerApprovalStep);
        Assert.NotNull(managerApproval);
        Assert.Equal(managerId, managerApproval.ApproverId);
        Assert.Equal(LeaveStatus.Approved, managerApproval.Status);

        var hrApproval = finalDbRequest.Approvals.FirstOrDefault(a => a.Step == LeaveApproval.HrApprovalStep);
        Assert.NotNull(hrApproval);
        Assert.Equal(hrUserId, hrApproval.ApproverId);
        Assert.Equal(LeaveStatus.Approved, hrApproval.Status);

        // 7. Verify notifications were recorded for the employee
        var employeeNotifications = await Db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == employeeId)
            .ToListAsync();

        Assert.NotEmpty(employeeNotifications);
    }
}
