using CompanyEmployees.Web.Models;

namespace CompanyEmployees.Web.Tests;

// The view-model the request list renders from. Small, but two of its members decide what the
// Actions column offers, and one of them shipped wrong: a cancelled row kept showing the
// "cancellation pending" marker because the date alone was treated as the signal.
public class TimeOffRequestTests
{
    [Fact]
    public void Cancellation_is_pending_while_the_leave_is_still_approved()
    {
        var request = new TimeOffRequest
        {
            Status = RequestStatus.Approved,
            CancellationRequestedAt = DateTime.UtcNow
        };

        Assert.True(request.CancellationPending);
    }

    [Fact]
    public void Cancellation_is_not_pending_once_HR_has_cancelled_the_leave()
    {
        // CancellationRequestedAt is kept after the decision — it records *when* the employee
        // asked — so on its own it does not mean anything is still outstanding. Reading it
        // alone left the pending marker lit on cancelled rows.
        var request = new TimeOffRequest
        {
            Status = RequestStatus.Cancelled,
            CancellationRequestedAt = DateTime.UtcNow.AddHours(-2)
        };

        Assert.False(request.CancellationPending);
    }

    [Fact]
    public void Nothing_is_pending_when_nobody_asked()
    {
        var request = new TimeOffRequest { Status = RequestStatus.Approved };

        Assert.False(request.CancellationPending);
    }

    [Fact]
    public void Days_counts_weekdays_only_when_no_working_day_count_was_supplied()
    {
        // Mon 2026-09-14 .. Sun 2026-09-20 is five weekdays.
        var request = new TimeOffRequest
        {
            StartDate = new DateOnly(2026, 9, 14),
            EndDate = new DateOnly(2026, 9, 20)
        };

        Assert.Equal(5, request.Days);
    }

    [Fact]
    public void Days_prefers_the_working_day_count_the_server_computed()
    {
        // The server's count also excludes regional public holidays, which this model cannot
        // know about — so when it is supplied it must win over the local weekday arithmetic.
        var request = new TimeOffRequest
        {
            StartDate = new DateOnly(2026, 9, 14),
            EndDate = new DateOnly(2026, 9, 20),
            WorkingDayCount = 3
        };

        Assert.Equal(3, request.Days);
    }

    [Fact]
    public void A_single_weekend_day_counts_as_no_days()
    {
        var saturday = new DateOnly(2026, 9, 19);
        var request = new TimeOffRequest { StartDate = saturday, EndDate = saturday };

        Assert.Equal(0, request.Days);
    }
}
