using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain.Entities;

public class LeaveApproval
{
    public Guid Id { get; set; }
    public Guid LeaveRequestId { get; set; }
    public LeaveRequest LeaveRequest { get; set; }
    public Guid ApproverId { get; set; }
    public User Approver { get; set; }
    public int Step { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}