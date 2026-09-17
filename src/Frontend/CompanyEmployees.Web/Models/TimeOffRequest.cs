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

    // When the owner asked HR to undo this leave. Kept after the decision as a record of
    // when they asked, so on its own it does not mean anything is still outstanding.
    public DateTime? CancellationRequestedAt { get; set; }

    // Still waiting on HR. The status stays Approved for as long as that is true, so pairing
    // the two is what separates "asked, unanswered" from "asked, and HR already cancelled it"
    // — the date alone stays set in both, which left the pending marker lit on cancelled rows.
    public bool CancellationPending =>
        CancellationRequestedAt is not null && Status == RequestStatus.Approved;

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
