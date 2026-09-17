using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain.Entities;


public class LeaveRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    // Why the leave was withdrawn: the requester's own note when they cancel a Pending
    // request, or their justification when they ask HR to undo an approved one.
    public string? CancellationReason { get; set; }

    // Set when the employee asks HR to undo leave that was already approved, and cleared
    // again if HR refuses. Status deliberately stays Approved while this is pending — the
    // balance counts Approved rows, so anything else would hand the days back before the
    // decision was actually made.
    public DateTime? CancellationRequestedAt { get; set; }
    public ICollection<LeaveApproval> Approvals { get; set; } = new List<LeaveApproval>();
    public DateTime CreatedAt { get; set; }
    public LeaveType Type { get; set; }
}
