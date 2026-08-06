using CompanyEmployees.Web.Models;

namespace CompanyEmployees.Web.Services;

/// <summary>
/// One teammate's absence on one specific day, pre-flattened so the calendar
/// can index absences per day in a single pass.
/// </summary>
public record TeamAbsence(string MemberName, string Initials, string Department, LeaveType Type, DateOnly Date);

/// <summary>One teammate's whole leave period (start–end), for the dashboard list and calendar views.</summary>
public record TeamTimeOff(string MemberName, string Initials, string Department, LeaveType Type, DateOnly StartDate, DateOnly EndDate, string? Team = null)
{
    public int Days => EndDate.DayNumber - StartDate.DayNumber + 1;
}

/// <summary>
/// One member of the user's team (the manager plus everyone sharing the same manager)
/// and their current-or-next approved leave. The leave fields are null when the
/// member has no upcoming leave.
/// </summary>
public record TeamRosterEntry(string Name, string Initials, string RoleLabel, bool IsManager,
    LeaveType? Type, DateOnly? Start, DateOnly? End);

public record RegionalHoliday(DateOnly Date, string Name);

// Async because the real implementation hits the database.
public interface ITimeOffService
{
    Task<TeamMember> GetCurrentUserAsync();
    Task<IReadOnlyList<LeaveBalance>> GetMyBalancesAsync();
    Task<IReadOnlyList<TimeOffRequest>> GetMyRequestsAsync();
    Task<IReadOnlyList<TeamAbsence>> GetTeamScheduleForMonthAsync(DateOnly monthStart);
    Task<IReadOnlyList<TeamTimeOff>> GetTeamTimeOffAsync();
    /// <summary>Teammates' leave periods overlapping [from, to], for the calendar's week/grid/list views.</summary>
    Task<IReadOnlyList<TeamTimeOff>> GetTeamTimeOffForRangeAsync(DateOnly from, DateOnly to);
    Task<IReadOnlyList<TeamRosterEntry>> GetTeamRosterAsync();
    Task<IReadOnlyList<RegionalHoliday>> GetRegionalHolidaysAsync(int year);
    Task<TimeOffRequest> SubmitRequestAsync(LeaveType type, DateOnly start, DateOnly end, string? reason);
}
