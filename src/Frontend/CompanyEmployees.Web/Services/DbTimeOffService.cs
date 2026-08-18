using CompanyEmployees.Application;
using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Web.Models;
using CompanyEmployees.Web.Security;
using Microsoft.AspNetCore.Components.Authorization;
using DomainEnums = CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Web.Services;

/// <summary>
/// Real implementation backed by EmployeeContext (Application layer).
/// Maps the Web view-models to/from the domain entities.
/// </summary>
public class DbTimeOffService : ITimeOffService
{
    private readonly EmployeeContext _employee;
    private readonly AuthenticationStateProvider _authStateProvider;
    private User? _currentUser; // cached per circuit (service is Scoped)
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DbTimeOffService(EmployeeContext employee, AuthenticationStateProvider authStateProvider)
    {
        _employee = employee;
        _authStateProvider = authStateProvider;
    }

    private async Task<User> GetDomainUserAsync()
    {
        if (_currentUser != null)
            return _currentUser;

        // UserName == Email for every account, so Identity.Name from the auth state is the email.
        var state = await _authStateProvider.GetAuthenticationStateAsync();
        var email = state.User.Identity?.Name
            ?? throw new InvalidOperationException("No authenticated user.");

        return _currentUser = await _employee.GetEmployeeByEmailAsync(email);
    }

    // Who is really at the keyboard. GetDomainUserAsync resolves the *borrowed* account while
    // an account is being borrowed, which is the right author for the request — but it says
    // nothing about the human, and that is what the audit row needs. Resolved here rather than
    // taken as a parameter so ITimeOffService (and the in-memory mock behind it) stay as they
    // are: delegation has no business on that interface.
    private async Task<ActingOnBehalf?> GetOnBehalfAsync()
    {
        var state = await _authStateProvider.GetAuthenticationStateAsync();
        var acting = ActingUser.Resolve(state.User);

        return acting is { IsImpersonating: true, DelegationId: { } delegationId }
            ? new ActingOnBehalf(acting.RealUserId, delegationId)
            : null;
    }

    public async Task<TeamMember> GetCurrentUserAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            return new TeamMember { Name = user.Name, Department = RoleAndDepartment(user) };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<LeaveBalance>> GetMyBalancesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            var balances = await _employee.GetMyBalancesAsync(user.Id, DateTime.Today.Year);

            return balances
                .Select(b => new LeaveBalance
                {
                    Type = MapType(b.Type),
                    DaysTotal = b.DaysTotal,
                    DaysUsed = b.DaysUsed,
                    CarriedOverDays = b.CarriedOverDays,
                    ExpiredCarriedOverDays = b.ExpiredCarriedOverDays,
                    CarryOverExpiryDate = b.CarryOverExpiryDate
                })
                .OrderBy(b => b.Type)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<TimeOffRequest>> GetMyRequestsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            var requests = await _employee.GetMyRequestsAsync(user.Id);
            var holidays = await GetHolidayDatesAsync(user, requests);
            return requests.Select(request => MapRequest(request, holidays)).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<TeamAbsence>> GetTeamScheduleForMonthAsync(DateOnly monthStart)
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var requests = await _employee.GetTeamRequestsAsync(user.Id, monthStart, monthEnd);

            // Flatten each request into one entry per day, clamped to the visible month.
            var absences = requests
                .SelectMany(r => DaysInRange(Max(r.StartDate, monthStart), Min(r.EndDate, monthEnd))
                    .Select(day => new TeamAbsence(
                        r.User.Name, Initials(r.User.Name), DepartmentName(r.User), MapType(r.Type), day)))
                .ToList();

            absences.AddRange((await GetOwnApprovedRequestsAsync(user, monthStart, monthEnd))
                .SelectMany(r => DaysInRange(Max(r.StartDate, monthStart), Min(r.EndDate, monthEnd))
                    .Select(day => new TeamAbsence(
                        user.Name, Initials(user.Name), DepartmentName(user), MapType(r.Type), day))));

            return absences;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<TeamTimeOff>> GetTeamTimeOffAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var requests = await _employee.GetTeamRequestsAsync(user.Id, today, today.AddMonths(3));

            return requests
                .Select(r => new TeamTimeOff(
                    r.User.Name, Initials(r.User.Name), DepartmentName(r.User),
                    MapType(r.Type), r.StartDate, r.EndDate, r.User.Role.ToString()))
                .OrderBy(t => t.StartDate)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<TeamTimeOff>> GetTeamTimeOffForRangeAsync(DateOnly from, DateOnly to)
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            var requests = await _employee.GetTeamRequestsAsync(user.Id, from, to);

            return requests
                .Select(r => new TeamTimeOff(
                    r.User.Name, Initials(r.User.Name), DepartmentName(r.User),
                    MapType(r.Type), r.StartDate, r.EndDate, r.User.Role.ToString()))
                .Concat((await GetOwnApprovedRequestsAsync(user, from, to))
                    .Select(r => new TeamTimeOff(
                        user.Name, Initials(user.Name), DepartmentName(user),
                        MapType(r.Type), r.StartDate, r.EndDate, user.Role.ToString())))
                .OrderBy(t => t.StartDate)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<TeamRosterEntry>> GetTeamRosterAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);

            var members = await _employee.GetTeamMembersAsync(user.Id);
            var requests = await _employee.GetTeamRequestsAsync(user.Id, today, today.AddMonths(3));
            var sortedRequests = requests.OrderBy(r => r.StartDate).ToList();

            var roster = new List<TeamRosterEntry>();
            foreach (var member in members)
            {
                // The member's current or next approved leave, if they have one.
                LeaveRequest? leave = null;
                foreach (var request in sortedRequests)
                {
                    if (request.UserId == member.Id)
                    {
                        leave = request;
                        break;
                    }
                }

                var isManager = member.Id == user.ManagerId;
                if (leave == null)
                {
                    roster.Add(new TeamRosterEntry(member.Name, Initials(member.Name),
                        RoleAndDepartment(member), isManager, null, null, null));
                }
                else
                {
                    roster.Add(new TeamRosterEntry(member.Name, Initials(member.Name),
                        RoleAndDepartment(member), isManager, MapType(leave.Type), leave.StartDate, leave.EndDate));
                }
            }
            return roster;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<RegionalHoliday>> GetRegionalHolidaysAsync(int year)
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            var holidays = await _employee.GetRegionalHolidaysAsync(user.Id, year);
            return holidays
                .Select(holiday => new RegionalHoliday(holiday.Date, holiday.Name))
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TimeOffRequest> SubmitRequestAsync(LeaveType type, DateOnly start, DateOnly end, string? reason)
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            var created = await _employee.SubmitRequestAsync(
                user.Id, MapTypeToDomain(type), start, end, reason, await GetOnBehalfAsync());
            var holidays = await GetHolidayDatesAsync(user, [created]);
            return MapRequest(created, holidays);
        }
        finally
        {
            _lock.Release();
        }
    }

    // --- mapping helpers -------------------------------------------------

    private async Task<HashSet<DateOnly>> GetHolidayDatesAsync(
        User user,
        IReadOnlyCollection<LeaveRequest> requests)
    {
        var holidays = new HashSet<DateOnly>();
        var years = requests
            .SelectMany(request => Enumerable.Range(
                request.StartDate.Year,
                request.EndDate.Year - request.StartDate.Year + 1))
            .Distinct();

        foreach (var year in years)
            foreach (var holiday in await _employee.GetRegionalHolidaysAsync(user.Id, year))
                holidays.Add(holiday.Date);

        return holidays;
    }

    private static TimeOffRequest MapRequest(LeaveRequest request, HashSet<DateOnly> holidays)
    {
        // The decision lives on the approvals; take the latest reviewed step.
        var decision = request.Approvals
            .Where(a => a.ReviewedAt != null)
            .OrderByDescending(a => a.Step)
            .FirstOrDefault();

        return new TimeOffRequest
        {
            Id = request.Id,
            Type = MapType(request.Type),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason,
            Status = MapStatus(request.Status),
            SubmittedAt = request.CreatedAt,
            DecidedBy = decision?.Approver?.Name,
            DecidedAt = decision?.ReviewedAt,
            WorkingDayCount = CountWorkingDays(request.StartDate, request.EndDate, holidays)
        };
    }

    private static int CountWorkingDays(DateOnly start, DateOnly end, HashSet<DateOnly> holidays)
    {
        var count = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                && !holidays.Contains(day))
                count++;
        return count;
    }

    // Explicit per-name mapping: the two enums do not share numeric order,
    // so a plain cast would silently mix the types up.
    private static LeaveType MapType(DomainEnums.LeaveType type) => type switch
    {
        DomainEnums.LeaveType.Annual => LeaveType.Annual,
        DomainEnums.LeaveType.Sick => LeaveType.Sick,
        DomainEnums.LeaveType.Parental => LeaveType.Parental,
        _ => LeaveType.Unpaid
    };

    private static DomainEnums.LeaveType MapTypeToDomain(LeaveType type) => type switch
    {
        LeaveType.Annual => DomainEnums.LeaveType.Annual,
        LeaveType.Sick => DomainEnums.LeaveType.Sick,
        LeaveType.Parental => DomainEnums.LeaveType.Parental,
        _ => DomainEnums.LeaveType.Unpaid
    };

    private static RequestStatus MapStatus(DomainEnums.LeaveStatus status) => status switch
    {
        DomainEnums.LeaveStatus.Pending => RequestStatus.Pending,
        DomainEnums.LeaveStatus.Approved => RequestStatus.Approved,
        DomainEnums.LeaveStatus.Rejected => RequestStatus.Rejected,
        _ => RequestStatus.Cancelled
    };

    private static string Initials(string name) => string.Concat(
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part[0]));

    // Users without a department (admins, the PM) show a dash rather than an empty label.
    private static string DepartmentName(User user) => user.Department?.Name ?? "—";

    /// <summary>
    /// The signed-in user's own approved leave overlapping a window.
    ///
    /// <c>EmployeeContext.GetTeamRequestsAsync</c> is the single definition of "team" — the
    /// manager plus everyone sharing a manager, <em>excluding the person asking</em>. Right for
    /// the Team page and the dashboard's "Team time off"; wrong for your own calendar, where it
    /// meant your approved days never appeared on it at all, and someone with no teammates saw
    /// an empty month. Composed here rather than by widening the team definition, which every
    /// other caller depends on.
    /// </summary>
    private async Task<List<LeaveRequest>> GetOwnApprovedRequestsAsync(User user, DateOnly from, DateOnly to)
    {
        var mine = await _employee.GetMyRequestsAsync(user.Id);

        return mine
            .Where(r => r.Status == DomainEnums.LeaveStatus.Approved
                        && r.StartDate <= to && r.EndDate >= from)
            .ToList();
    }

    private static string RoleAndDepartment(User user) =>
        user.Department == null ? user.Role.ToString() : $"{user.Role} · {user.Department.Name}";

    private static IEnumerable<DateOnly> DaysInRange(DateOnly start, DateOnly end)
    {
        for (var day = start; day <= end; day = day.AddDays(1))
            yield return day;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
