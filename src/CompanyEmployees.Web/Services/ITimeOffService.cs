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

public interface ITimeOffService
{
    TeamMember CurrentUser { get; }
    IReadOnlyList<LeaveBalance> GetMyBalances();
    IReadOnlyList<TimeOffRequest> GetMyRequests();
    IReadOnlyList<TeamAbsence> GetTeamScheduleForMonth(DateOnly monthStart);
    IReadOnlyList<TeamTimeOff> GetTeamTimeOff();
    TimeOffRequest SubmitRequest(LeaveType type, DateOnly start, DateOnly end, string? reason);
}
