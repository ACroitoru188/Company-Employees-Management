using CompanyEmployees.Web.Models;

namespace CompanyEmployees.Web.Services;

/// <summary>
/// Mock implementation seeded with demo data. Scoped per circuit: submitted requests
/// survive navigation between the employee pages but reset on a full page reload.
/// Swap for a real API-backed implementation without touching the Razor components.
/// </summary>
public class InMemoryTimeOffService : ITimeOffService
{
    private readonly List<LeaveBalance> _balances;
    private readonly List<TimeOffRequest> _myRequests;
    private readonly List<TeamMember> _team;

    private readonly TeamMember _currentUser = new() { Name = "Anna Keller", Department = "Marketing" };

    public InMemoryTimeOffService()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = new DateOnly(today.Year, today.Month, 1);

        _balances =
        [
            new LeaveBalance { Type = LeaveType.Annual, DaysUsed = 9, DaysTotal = 24 },
            new LeaveBalance { Type = LeaveType.Sick, DaysUsed = 10, DaysTotal = 10 },
            new LeaveBalance { Type = LeaveType.Parental, DaysUsed = 0, DaysTotal = 16 },
            new LeaveBalance { Type = LeaveType.Unpaid, DaysUsed = 1, DaysTotal = 5 }
        ];

        // Dates are relative to the current month so the demo data never goes stale.
        _team =
        [
            Member("Wei Zhang", "Engineering", "Platform", LeaveType.Annual, month.AddDays(12), month.AddDays(16)),
            Member("Priya Nair", "Design", "Product", LeaveType.Sick, month.AddDays(8), month.AddDays(9)),
            Member("Julia Novak", "Design", "Brand", LeaveType.Annual, month.AddDays(19), month.AddDays(23)),
            Member("Lukas Becker", "Engineering", "Platform", LeaveType.Parental, month.AddDays(26), month.AddMonths(1).AddDays(-1)),
            Member("Marco Rossi", "Engineering", "Web", LeaveType.Annual, month.AddMonths(1).AddDays(2), month.AddMonths(1).AddDays(6)),
            Member("Elena Petrova", "Marketing", "Growth", LeaveType.Unpaid, month.AddDays(14), month.AddDays(15))
        ];

        _myRequests =
        [
            new TimeOffRequest
            {
                Type = LeaveType.Annual,
                StartDate = month.AddMonths(1).AddDays(2),
                EndDate = month.AddMonths(1).AddDays(6),
                Status = RequestStatus.Pending,
                SubmittedAt = DateTime.Now.AddDays(-2).AddHours(-3)
            },
            new TimeOffRequest
            {
                Type = LeaveType.Sick,
                StartDate = today.AddDays(-12),
                EndDate = today.AddDays(-11),
                Reason = "Flu",
                Status = RequestStatus.Approved,
                SubmittedAt = DateTime.Now.AddDays(-13),
                DecidedBy = "D. Novak",
                DecidedAt = DateTime.Now.AddDays(-12)
            },
            new TimeOffRequest
            {
                Type = LeaveType.Annual,
                StartDate = today.AddDays(-40),
                EndDate = today.AddDays(-36),
                Reason = "Summer vacation",
                Status = RequestStatus.Approved,
                SubmittedAt = DateTime.Now.AddDays(-55),
                DecidedBy = "D. Novak",
                DecidedAt = DateTime.Now.AddDays(-53)
            },
            new TimeOffRequest
            {
                Type = LeaveType.Parental,
                StartDate = today.AddDays(-70),
                EndDate = today.AddDays(-57),
                Status = RequestStatus.Rejected,
                Reason = "Family support",
                SubmittedAt = DateTime.Now.AddDays(-85),
                DecidedBy = "D. Novak",
                DecidedAt = DateTime.Now.AddDays(-83)
            },
            new TimeOffRequest
            {
                Type = LeaveType.Unpaid,
                StartDate = today.AddDays(-25),
                EndDate = today.AddDays(-25),
                Status = RequestStatus.Cancelled,
                SubmittedAt = DateTime.Now.AddDays(-30)
            }
        ];
    }

    public Task<TeamMember> GetCurrentUserAsync() => Task.FromResult(_currentUser);

    public Task<IReadOnlyList<LeaveBalance>> GetMyBalancesAsync() =>
        Task.FromResult<IReadOnlyList<LeaveBalance>>(_balances);

    public Task<IReadOnlyList<TimeOffRequest>> GetMyRequestsAsync() =>
        Task.FromResult<IReadOnlyList<TimeOffRequest>>(
            _myRequests.OrderByDescending(r => r.SubmittedAt).ToList());

    public Task<IReadOnlyList<TeamAbsence>> GetTeamScheduleForMonthAsync(DateOnly monthStart)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        IReadOnlyList<TeamAbsence> result = _team
            .SelectMany(m => m.Requests, (m, r) => (Member: m, Request: r))
            .SelectMany(x => DaysInRange(
                    Max(x.Request.StartDate, monthStart), Min(x.Request.EndDate, monthEnd))
                .Select(day => new TeamAbsence(
                    x.Member.Name, x.Member.Initials, x.Member.Department, x.Request.Type, day)))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TeamTimeOff>> GetTeamTimeOffAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        IReadOnlyList<TeamTimeOff> result = _team
            .SelectMany(m => m.Requests,
                (m, r) => new TeamTimeOff(m.Name, m.Initials, m.Department, r.Type, r.StartDate, r.EndDate))
            .Where(t => t.EndDate >= today)
            .OrderBy(t => t.StartDate)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TeamTimeOff>> GetTeamTimeOffForRangeAsync(DateOnly from, DateOnly to)
    {
        IReadOnlyList<TeamTimeOff> result = _team
            .SelectMany(m => m.Requests,
                (m, r) => new TeamTimeOff(m.Name, m.Initials, m.Department, r.Type, r.StartDate, r.EndDate, m.Team))
            .Where(t => t.StartDate <= to && t.EndDate >= from)
            .OrderBy(t => t.StartDate)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TeamRosterEntry>> GetTeamRosterAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var roster = new List<TeamRosterEntry>();
        foreach (var member in _team)
        {
            var leave = member.Requests
                .Where(r => r.EndDate >= today)
                .OrderBy(r => r.StartDate)
                .FirstOrDefault();
            if (leave == null)
                roster.Add(new TeamRosterEntry(member.Name, member.Initials, member.Department,
                    false, null, null, null));
            else
                roster.Add(new TeamRosterEntry(member.Name, member.Initials, member.Department,
                    false, leave.Type, leave.StartDate, leave.EndDate));
        }
        return Task.FromResult<IReadOnlyList<TeamRosterEntry>>(roster);
    }

    public Task<IReadOnlyList<RegionalHoliday>> GetRegionalHolidaysAsync(int year) =>
        Task.FromResult<IReadOnlyList<RegionalHoliday>>([]);

    public Task<TimeOffRequest> SubmitRequestAsync(LeaveType type, DateOnly start, DateOnly end, string? reason)
    {
        var request = new TimeOffRequest
        {
            Type = type,
            StartDate = start,
            EndDate = end,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Status = RequestStatus.Pending,
            SubmittedAt = DateTime.Now
        };
        _myRequests.Add(request);
        return Task.FromResult(request);
    }

    private static TeamMember Member(string name, string department, string team, LeaveType type, DateOnly start, DateOnly end) =>
        new()
        {
            Name = name,
            Department = department,
            Team = team,
            Requests =
            [
                new TimeOffRequest
                {
                    Type = type,
                    StartDate = start,
                    EndDate = end,
                    Status = RequestStatus.Approved,
                    SubmittedAt = DateTime.Now.AddDays(-20)
                }
            ]
        };

    private static IEnumerable<DateOnly> DaysInRange(DateOnly start, DateOnly end)
    {
        for (var day = start; day <= end; day = day.AddDays(1))
            yield return day;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
