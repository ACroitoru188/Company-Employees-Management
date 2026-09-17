using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
// The domain defines its own InvalidOperationException; the alias picks it over System's.
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Contexts
{
    public class AdminContext : BaseContext
    {
        private readonly IUserGateway _userGateway;
        private readonly IDepartmentGateway _departmentGateway;
        private readonly IRegionGateway _regionGateway;
        private readonly IContractGateway _contractGateway;
        private readonly DelegationGuard _delegationGuard;

        public AdminContext(
            ILogger<AdminContext> logger,
            IUserGateway userGateway,
            IDepartmentGateway departmentGateway,
            IRegionGateway regionGateway,
            IContractGateway contractGateway,
            DelegationGuard delegationGuard) : base(logger)
        {
            _userGateway = userGateway;
            _departmentGateway = departmentGateway;
            _regionGateway = regionGateway;
            _contractGateway = contractGateway;
            _delegationGuard = delegationGuard;
        }

        private Task<ManagerDelegation?> GuardAsync(Guid actingAsUserId, ActingOnBehalf? onBehalf) =>
            _delegationGuard.GuardAsync(actingAsUserId, onBehalf);

        private Task RecordDelegatedActionAsync(
            ManagerDelegation? delegation, Guid actingAsUserId, Guid targetUserId,
            DelegatedActionType actionType, Guid targetEntityId, string? details) =>
            _delegationGuard.RecordDelegatedActionAsync(delegation, actingAsUserId, targetUserId, actionType, targetEntityId, details);


        public Task<List<Department>> GetDepartmentsAsync() =>
            _departmentGateway.GetAllAsync();

        public Task<List<Region>> GetRegionsAsync(bool activeOnly = false) =>
            _regionGateway.GetAllAsync(activeOnly);

        public Task<List<User>> GetAllUsersAsync() =>
            _userGateway.GetAllUsersAsync();

        public async Task<List<User>> GetUsersInMyRegionAsync(Guid userId)
        {
            var requester = await _userGateway.GetUserByIdAsync(userId);
            if (requester == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            return (await _userGateway.GetAllUsersAsync())
                .Where(user => user.RegionId == requester.RegionId)
                .ToList();
        }

        public async Task<Department> CreateDepartmentAsync(Guid adminId, string name, Guid? managerId)
        {
            await EnsureAdminAsync(adminId);

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Department name is required.");
            if (managerId is null)
                throw new InvalidOperationException("Select a manager for the department.");

            var trimmedName = name.Trim();
            await EnsureNoDuplicateNameAsync(trimmedName, excludingId: null);

            var department = new Department { Name = trimmedName, ManagerId = managerId };
            await _departmentGateway.CreateAsync(department);

            _logger.LogInformation("Admin {AdminId} created department {DepartmentId} (\"{DepartmentName}\") with manager {ManagerId}.",
                adminId, department.Id, department.Name, managerId);

            return department;
        }

        public async Task UpdateDepartmentAsync(Guid adminId, Guid id, string name, Guid? managerId)
        {
            await EnsureAdminAsync(adminId);

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Department name is required.");

            var department = await _departmentGateway.GetByIdAsync(id);
            if (department == null)
                throw new EntityNotFoundException($"No department with id {id}.");

            var trimmedName = name.Trim();
            await EnsureNoDuplicateNameAsync(trimmedName, excludingId: id);

            department.Name = trimmedName;
            department.ManagerId = managerId;
            await _departmentGateway.UpdateAsync(department);

            _logger.LogInformation("Admin {AdminId} updated department {DepartmentId} (\"{DepartmentName}\") with manager {ManagerId}.",
                adminId, id, trimmedName, managerId);
        }

        public async Task DeleteDepartmentAsync(Guid adminId, Guid id)
        {
            await EnsureAdminAsync(adminId);
            await _departmentGateway.DeleteAsync(id);

            _logger.LogInformation("Admin {AdminId} deleted department {DepartmentId}.", adminId, id);
        }

        private async Task EnsureNoDuplicateNameAsync(string name, Guid? excludingId)
        {
            var existing = await _departmentGateway.GetAllAsync();
            var collides = existing.Any(department =>
                department.Id != excludingId
                && string.Equals(department.Name, name, StringComparison.OrdinalIgnoreCase));

            if (collides)
                throw new InvalidOperationException($"A department named \"{name}\" already exists.");
        }

        private async Task EnsureAdminAsync(Guid adminId)
        {
            var admin = await _userGateway.GetUserByIdAsync(adminId);
            if (admin == null)
                throw new EntityNotFoundException($"No administrator with id {adminId}.");
            if (admin.Role != UserRole.Admin)
                throw new UnauthorizedException("Only administrators can manage departments.");
        }

        public async Task AssignUserToDepartmentAsync(
            Guid adminId, Guid userId, Guid? departmentId, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(adminId, onBehalf);
            var user = await EnsureRegionalAdminCanManageAsync(adminId, userId);

            var oldDeptName = user.Department?.Name ?? "None";
            user.DepartmentId = departmentId;
            user.Department = departmentId.HasValue ? await _departmentGateway.GetByIdAsync(departmentId.Value) : null;
            var newDeptName = user.Department?.Name ?? "None";
            await _userGateway.UpdateUserAsync(user);

            await RecordDelegatedActionAsync(
                delegation, adminId, userId, DelegatedActionType.DepartmentChanged,
                departmentId ?? Guid.Empty, $"Department: {oldDeptName} → {newDeptName}");

            _logger.LogInformation("Admin {AdminId} assigned user {UserId} to department {DepartmentName} ({DepartmentId}).",
                adminId, userId, newDeptName, departmentId);
        }

        public async Task AssignUserToRegionAsync(
            Guid adminId, Guid userId, Guid regionId, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(adminId, onBehalf);
            var region = await _regionGateway.GetByIdAsync(regionId);
            if (region == null || !region.IsActive)
                throw new InvalidOperationException("Select a valid active region.");

            // The source-region admin owns the transfer. Once the employee moves,
            // only an administrator in the destination region may edit them.
            var user = await EnsureRegionalAdminCanManageAsync(adminId, userId);
            if (user.RegionId == regionId)
                return;

            var oldRegionName = user.Region?.Name ?? "Unknown";
            user.RegionId = regionId;
            user.Region = region;

            // A relocation must not preserve cross-region reporting relationships.
            if (user.ManagerId is Guid managerId)
            {
                var manager = await _userGateway.GetUserByIdAsync(managerId);
                if (manager?.RegionId != regionId)
                {
                    user.ManagerId = null;
                    user.Manager = null;
                }
            }

            user.SecurityStamp = Guid.NewGuid().ToString("D");
            user.UpdatedAt = DateTime.UtcNow;
            await _userGateway.UpdateUserAsync(user);

            var directReports = await _userGateway.GetAllDirectReportsAsync(userId);
            foreach (var report in directReports.Where(report => report.RegionId != regionId))
            {
                report.ManagerId = null;
                report.Manager = null;
                report.UpdatedAt = DateTime.UtcNow;
                await _userGateway.UpdateUserAsync(report);
            }

            await RecordDelegatedActionAsync(
                delegation, adminId, userId, DelegatedActionType.RegionChanged,
                regionId, $"Region: {oldRegionName} → {region.Name}");

            _logger.LogInformation("Admin {AdminId} transferred user {UserId} to region {RegionName} ({RegionId}).",
                adminId, userId, region.Name, regionId);
        }

        public async Task<Contract?> GetActiveContractForUserAsync(Guid userId)
        {
            return await _contractGateway.GetActiveContractByUserIdAsync(userId);
        }

        public async Task SaveUserContractAsync(
            Guid adminId,
            Guid userId,
            ContractType type,
            ContractStatus status,
            DateOnly startDate,
            DateOnly? endDate,
            string? notes,
            ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(adminId, onBehalf);
            await EnsureRegionalAdminCanManageAsync(adminId, userId);

            var active = await _contractGateway.GetActiveContractByUserIdAsync(userId);
            bool isExtension = false;
            string details;
            Guid contractId;

            if (active != null)
            {
                var prevEnd = active.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Indefinite";
                isExtension = type == ContractType.Determinate && endDate.HasValue && active.EndDate.HasValue && endDate.Value > active.EndDate.Value;
                active.Type = type;
                active.Status = status;
                active.StartDate = startDate;
                active.EndDate = type == ContractType.Indeterminate ? null : endDate;
                active.Notes = notes;
                active.UpdatedAt = DateTime.UtcNow;
                await _contractGateway.UpdateAsync(active);
                contractId = active.Id;
                details = isExtension
                    ? $"End date {prevEnd} → {endDate:yyyy-MM-dd}"
                    : $"Type: {type}, Status: {status}, Period: {startDate:yyyy-MM-dd} – {(active.EndDate.HasValue ? active.EndDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Indefinite")}";
            }
            else
            {
                var newContract = new Contract
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Type = type,
                    Status = status,
                    StartDate = startDate,
                    EndDate = type == ContractType.Indeterminate ? null : endDate,
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _contractGateway.CreateAsync(newContract);
                contractId = newContract.Id;
                details = $"Created contract ({type}, {status}), Period: {startDate:yyyy-MM-dd} – {(newContract.EndDate.HasValue ? newContract.EndDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Indefinite")}";
            }

            await RecordDelegatedActionAsync(
                delegation, adminId, userId,
                isExtension ? DelegatedActionType.ContractExtended : DelegatedActionType.ContractUpdated,
                contractId, details);
        }

        private async Task<User> EnsureRegionalAdminCanManageAsync(Guid adminId, Guid userId)
        {
            var admin = await _userGateway.GetUserByIdAsync(adminId);
            if (admin == null)
                throw new EntityNotFoundException($"No administrator with id {adminId}.");
            if (admin.Role != UserRole.Admin && admin.Role != UserRole.CountryManager)
                throw new UnauthorizedException("Only administrators and country managers can manage employee accounts.");

            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");
            if (user.RegionId != admin.RegionId)
                throw new UnauthorizedException("You can preview other regions, but you cannot edit their employees.");

            return user;
        }
    }
}
