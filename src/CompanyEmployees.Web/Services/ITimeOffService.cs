using CompanyEmployees.Web.Models;

namespace CompanyEmployees.Web.Services;

/// <summary>
/// One teammate's absence on one specific day, pre-flattened so the calendar
/// can index absences per day in a single pass.
/// </summary>
public record TeamAbsence(string MemberName, string Initials, string Department, LeaveType Type, DateOnly Date);

/// <summary>One teammate's whole leave period (start–end), for the dashboard list.</summary>
public record TeamTimeOff(string MemberName, string Initials, string Department, LeaveType Type, DateOnly StartDate, DateOnly EndDate)
{
    public int Days => EndDate.DayNumber - StartDate.DayNumber + 1;
}

// Async because the real implementation hits the database.
public interface ITimeOffService
{
    Task<TeamMember> GetCurrentUserAsync();
    Task<IReadOnlyList<LeaveBalance>> GetMyBalancesAsync();
    Task<IReadOnlyList<TimeOffRequest>> GetMyRequestsAsync();
    Task<IReadOnlyList<TeamAbsence>> GetTeamScheduleForMonthAsync(DateOnly monthStart);
    Task<IReadOnlyList<TeamTimeOff>> GetTeamTimeOffAsync();
    Task<TimeOffRequest> SubmitRequestAsync(LeaveType type, DateOnly start, DateOnly end, string? reason);
}
