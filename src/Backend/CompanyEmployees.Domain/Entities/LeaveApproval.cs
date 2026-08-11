using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain.Entities;

public class LeaveApproval
{
    // Which required approver a row represents, per LeaveApprovalPolicy — a request can
    // carry one of each when both are required (see LeaveApprovalPolicy.IsFullyApproved).
    public const int ManagerApprovalStep = 1;
    public const int HrApprovalStep = 2;

    public Guid Id { get; set; }
    public Guid LeaveRequestId { get; set; }
    public LeaveRequest LeaveRequest { get; set; } = null!;
    public Guid ApproverId { get; set; }
    public User Approver { get; set; } = null!;
    public int Step { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
