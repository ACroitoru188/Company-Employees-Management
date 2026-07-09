using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain.Entities;


public class LeaveRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public ICollection<LeaveApproval> Approvals { get; set; } = new List<LeaveApproval>();
    public DateTime CreatedAt { get; set; }
}
