using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
// The domain defines its own InvalidOperationException; the alias picks it over System's.
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Contexts
{
    public class EmployeeContext : BaseContext
    {
        private readonly ILeaveRequestGateway _leaveRequestGateway;
        private readonly IUserGateway _userGateway;
        private readonly IDepartmentGateway _departmentGateway;

        public EmployeeContext(
            ILogger<EmployeeContext> logger,
            ILeaveRequestGateway leaveRequestGateway,
            IUserGateway userGateway,
            IDepartmentGateway departmentGateway) : base(logger)
        {
            _leaveRequestGateway = leaveRequestGateway;
            _userGateway = userGateway;
            _departmentGateway = departmentGateway;
        }

        public async Task<User> GetEmployeeByEmailAsync(string email)
        {
            var user = await _userGateway.GetUserByEmailAsync(email);
            if (user == null)
                throw new EntityNotFoundException($"No user with email {email}.");
            return user;
        }

        public Task<List<LeaveRequest>> GetMyRequestsAsync(Guid userId)
        {
            return _leaveRequestGateway.GetRequestsByUserAsync(userId);
        }

        public async Task<List<LeaveBalanceResult>> GetMyBalancesAsync(Guid userId, int year)
        {
            var allocations = await _leaveRequestGateway.GetAllocationsByUserAsync(userId, year);
            var requests = await _leaveRequestGateway.GetRequestsByUserAsync(userId);

            var balances = new List<LeaveBalanceResult>();
            foreach (var allocation in allocations)
            {
                var daysUsed = requests
                    .Where(r => r.Status == LeaveStatus.Approved
                                && r.Type == allocation.LeaveType
                                && r.StartDate.Year == year)
                    .Sum(r => CountDays(r.StartDate, r.EndDate));

                balances.Add(new LeaveBalanceResult
                {
                    Type = allocation.LeaveType,
                    DaysTotal = allocation.NumberOfDays,
                    DaysUsed = daysUsed
                });
            }
            return balances;
        }

        // Approved leave of the user's team (same manager, excluding the user,
        // plus the manager themself) that touches the [from, to] interval.
        public async Task<List<LeaveRequest>> GetTeamRequestsAsync(Guid userId, DateOnly from, DateOnly to)
        {
            var team = await GetTeamMembersAsync(userId);

            var teamIds = new List<Guid>();
            foreach (var member in team)
            {
                teamIds.Add(member.Id);
            }

            if (teamIds.Count == 0)
                return [];

            return await _leaveRequestGateway.GetApprovedRequestsForUsersAsync(teamIds, from, to);
        }

        // The user's team: their manager first, then the active colleagues who
        // share the same manager. A user without a manager has no team.
        public async Task<List<User>> GetTeamMembersAsync(Guid userId)
        {
            var me = await _userGateway.GetUserByIdAsync(userId);
            if (me == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            var team = new List<User>();
            if (me.ManagerId == null)
                return team;

            var manager = await _userGateway.GetUserByIdAsync(me.ManagerId.Value);
            if (manager != null)
                team.Add(manager);

            var allUsers = await _userGateway.GetAllUsersAsync();
            foreach (var user in allUsers)
            {
                if (user.ManagerId == me.ManagerId && user.Id != userId && user.Status == UserStatus.Active)
                    team.Add(user);
            }
            return team;
        }

        // --- administrare departamente (folosit de pagina admin crud) -----------------

        public Task<List<Department>> GetDepartmentsAsync() =>
            _departmentGateway.GetAllAsync();

        public Task<List<User>> GetAllUsersAsync() =>
            _userGateway.GetAllUsersAsync();

        public async Task<Department> CreateDepartmentAsync(string name, Guid? managerId)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Department name is required.");

            var department = new Department { Name = name.Trim(), ManagerId = managerId };
            await _departmentGateway.CreateAsync(department);
            return department;
        }

        public async Task UpdateDepartmentAsync(Guid id, string name, Guid? managerId)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Department name is required.");

            var department = await _departmentGateway.GetByIdAsync(id);
            if (department == null)
                throw new EntityNotFoundException($"No department with id {id}.");

            department.Name = name.Trim();
            department.ManagerId = managerId;
            await _departmentGateway.UpdateAsync(department);
        }

        public Task DeleteDepartmentAsync(Guid id) =>
            _departmentGateway.DeleteAsync(id);

        public async Task AssignUserToDepartmentAsync(Guid userId, Guid? departmentId)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            user.DepartmentId = departmentId;
            await _userGateway.UpdateUserAsync(user);
        }

        public async Task<LeaveRequest> SubmitRequestAsync(
            Guid userId, LeaveType type, DateOnly start, DateOnly end, string? reason)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            if (end < start)
                throw new InvalidOperationException("End date must not be before start date.");
            if (start < today)
                throw new InvalidOperationException("Leave cannot start in the past.");

            var existing = await _leaveRequestGateway.GetRequestsByUserAsync(userId);
            var overlaps = existing.Any(r =>
                (r.Status == LeaveStatus.Pending || r.Status == LeaveStatus.Approved)
                && r.StartDate <= end
                && r.EndDate >= start);
            if (overlaps)
                throw new InvalidOperationException("You already have a request in that period.");

            var requestedDays = CountDays(start, end);
            var balances = await GetMyBalancesAsync(userId, start.Year);
            var balance = balances.FirstOrDefault(b => b.Type == type);
            if (balance == null || balance.DaysTotal - balance.DaysUsed < requestedDays)
                throw new InvalidOperationException("Not enough days left for this leave type.");

            var request = new LeaveRequest
            {
                UserId = userId,
                Type = type,
                StartDate = start,
                EndDate = end,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                Status = LeaveStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            await _leaveRequestGateway.CreateRequestAsync(request);

            _logger.LogInformation("User {UserId} submitted a {Type} leave request {Start}–{End}.",
                userId, type, start, end);
            return request;
        }

        private static int CountDays(DateOnly start, DateOnly end) =>
            end.DayNumber - start.DayNumber + 1;
    }
}
