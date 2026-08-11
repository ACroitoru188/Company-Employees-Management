using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain.Tests;

public class LeaveApprovalPolicyTests
{
    [Fact]
    public void DetermineRequirement_auto_approves_admin_requests()
    {
        var requester = User(UserRole.Admin);

        var requirement = LeaveApprovalPolicy.DetermineRequirement(requester);

        Assert.Equal(new ApprovalRequirement(false, false, true), requirement);
    }

    [Fact]
    public void DetermineRequirement_requires_only_manager_for_hr_staff()
    {
        var requester = User(UserRole.Employee);
        requester.Department = new Department { Name = LeaveApprovalPolicy.HrDepartmentName };
        requester.Manager = User(UserRole.LineManager);

        var requirement = LeaveApprovalPolicy.DetermineRequirement(requester);

        Assert.Equal(new ApprovalRequirement(true, false, false), requirement);
    }

    [Fact]
    public void DetermineRequirement_requires_manager_and_hr_for_employee_with_line_manager()
    {
        var requester = User(UserRole.Employee);
        requester.Manager = User(UserRole.LineManager);

        var requirement = LeaveApprovalPolicy.DetermineRequirement(requester);

        Assert.Equal(new ApprovalRequirement(true, true, false), requirement);
    }

    [Fact]
    public void DetermineRequirement_requires_only_hr_when_manager_is_not_line_manager()
    {
        var requester = User(UserRole.Employee);
        requester.Manager = User(UserRole.Admin);

        var requirement = LeaveApprovalPolicy.DetermineRequirement(requester);

        Assert.Equal(new ApprovalRequirement(false, true, false), requirement);
    }

    [Fact]
    public void IsFullyApproved_returns_false_when_required_hr_approval_is_missing()
    {
        var request = RequestWithApproval(LeaveApproval.ManagerApprovalStep);
        var requirement = new ApprovalRequirement(true, true, false);

        var result = LeaveApprovalPolicy.IsFullyApproved(request, requirement);

        Assert.False(result);
    }

    [Fact]
    public void IsFullyApproved_returns_true_when_all_required_steps_are_approved()
    {
        var request = RequestWithApproval(
            LeaveApproval.ManagerApprovalStep,
            LeaveApproval.HrApprovalStep);
        var requirement = new ApprovalRequirement(true, true, false);

        var result = LeaveApprovalPolicy.IsFullyApproved(request, requirement);

        Assert.True(result);
    }

    [Fact]
    public void IsFullyApproved_ignores_rejected_rows_when_checking_approval()
    {
        var request = new LeaveRequest
        {
            Approvals =
            [
                new LeaveApproval
                {
                    Step = LeaveApproval.ManagerApprovalStep,
                    Status = LeaveStatus.Rejected
                }
            ]
        };
        var requirement = new ApprovalRequirement(true, false, false);

        var result = LeaveApprovalPolicy.IsFullyApproved(request, requirement);

        Assert.False(result);
    }

    private static User User(UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        Name = role.ToString(),
        Role = role
    };

    private static LeaveRequest RequestWithApproval(params int[] steps) => new()
    {
        Approvals = steps.Select(step => new LeaveApproval
        {
            Step = step,
            Status = LeaveStatus.Approved
        }).ToList()
    };
}
