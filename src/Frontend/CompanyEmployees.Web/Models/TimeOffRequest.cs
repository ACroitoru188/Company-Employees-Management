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
    public string? CancellationReason { get; set; }
    public int? WorkingDayCount { get; set; }

    public int Days => WorkingDayCount ?? CountWeekdays();

    private int CountWeekdays()
    {
        var count = 0;
        for (var day = StartDate; day <= EndDate; day = day.AddDays(1))
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                count++;
        return count;
    }
}
