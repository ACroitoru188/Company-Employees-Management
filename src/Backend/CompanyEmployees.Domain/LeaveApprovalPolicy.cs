using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain;

// Centralizes "who has to approve this person's leave" so the manager-decision path,
// the HR-decision path, and the two dashboards' "still needs my action" lists all agree
// on the same rule instead of drifting apart. Lives here (not Application) so both
// Gateway (dashboard queries) and Application (decision logic) can reference it without
// a Gateway -> Application dependency, which the project layout doesn't allow.
//
//   Admin                                              -> nobody; auto-approved on submit
//   HR-department staff (not the HR department's own
//   LineManager)                                        -> their manager only, no HR review
//   everyone else, manager IS a LineManager              -> manager AND HR
//   everyone else, manager is NOT a LineManager
//     (no manager, or manager is an Admin)                -> HR only
//
// "Manager" only counts as a required approver when Manager.Role == LineManager — an
// Admin-manager sits outside this workflow (there's no approve/reject UI for Admins).
public static class LeaveApprovalPolicy
{
    public const string HrDepartmentName = "HR";

    public static ApprovalRequirement DetermineRequirement(User requester)
    {
        if (requester.Role == UserRole.Admin)
            return new ApprovalRequirement(NeedsManagerApproval: false, NeedsHrApproval: false, AutoApproved: true);

        var isHrStaff = requester.Role != UserRole.LineManager
                         && requester.Department?.Name == HrDepartmentName;
        if (isHrStaff)
            return new ApprovalRequirement(NeedsManagerApproval: true, NeedsHrApproval: false, AutoApproved: false);

        var managerIsLineManager = requester.Manager?.Role == UserRole.LineManager;
        return new ApprovalRequirement(NeedsManagerApproval: managerIsLineManager, NeedsHrApproval: true, AutoApproved: false);
    }

    // True once every required approval has a matching Approved row. Call after appending
    // this decision's own (not-yet-saved) LeaveApproval to request.Approvals, so a
    // just-recorded decision counts without needing a round trip to the database.
    public static bool IsFullyApproved(LeaveRequest request, ApprovalRequirement requirement)
    {
        var managerDone = !requirement.NeedsManagerApproval
            || request.Approvals.Any(a => a.Step == LeaveApproval.ManagerApprovalStep && a.Status == LeaveStatus.Approved);
        var hrDone = !requirement.NeedsHrApproval
            || request.Approvals.Any(a => a.Step == LeaveApproval.HrApprovalStep && a.Status == LeaveStatus.Approved);
        return managerDone && hrDone;
    }
}

public record ApprovalRequirement(bool NeedsManagerApproval, bool NeedsHrApproval, bool AutoApproved);
