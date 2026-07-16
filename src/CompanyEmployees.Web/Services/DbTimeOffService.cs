using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Web.Models;
using DomainEnums = CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Web.Services;

/// <summary>
/// Real implementation backed by EmployeeContext (Application layer).
/// Maps the Web view-models to/from the domain entities.
/// </summary>
public class DbTimeOffService : ITimeOffService
{
    // TODO: replace with the authenticated user once login issues a real session
    // (see AuthenticationContext — Blazor login is still a mock).
    private const string CurrentUserEmail = "employee@siemens.com";

    private readonly EmployeeContext _employee;
    private User? _currentUser; // cached per circuit (service is Scoped)
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DbTimeOffService(EmployeeContext employee)
    {
        _employee = employee;
    }

    private async Task<User> GetDomainUserAsync() =>
        _currentUser ??= await _employee.GetEmployeeByEmailAsync(CurrentUserEmail);

    public async Task<TeamMember> GetCurrentUserAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var user = await GetDomainUserAsync();
            // The domain has no Department yet; show the role in its place.
            return new TeamMember { Name = user.Name, Department = user.Role.ToString() };
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
                    DaysUsed = b.DaysUsed
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
            return requests.Select(MapRequest).ToList();
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
            return requests
                .SelectMany(r => DaysInRange(Max(r.StartDate, monthStart), Min(r.EndDate, monthEnd))
                    .Select(day => new TeamAbsence(
                        r.User.Name, Initials(r.User.Name), r.User.Role.ToString(), MapType(r.Type), day)))
                .ToList();
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
                    r.User.Name, Initials(r.User.Name), r.User.Role.ToString(),
                    MapType(r.Type), r.StartDate, r.EndDate))
                .OrderBy(t => t.StartDate)
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
            var created = await _employee.SubmitRequestAsync(user.Id, MapTypeToDomain(type), start, end, reason);
            return MapRequest(created);
        }
        finally
        {
            _lock.Release();
        }
    }

    // --- mapping helpers -------------------------------------------------

    private static TimeOffRequest MapRequest(LeaveRequest request)
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
            DecidedAt = decision?.ReviewedAt
        };
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

    private static IEnumerable<DateOnly> DaysInRange(DateOnly start, DateOnly end)
    {
        for (var day = start; day <= end; day = day.AddDays(1))
            yield return day;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
