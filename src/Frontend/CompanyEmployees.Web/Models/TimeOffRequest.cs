namespace CompanyEmployees.Web.Models;

public class TimeOffRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public LeaveType Type { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTime SubmittedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }

    public int Days => EndDate.DayNumber - StartDate.DayNumber + 1;
}
