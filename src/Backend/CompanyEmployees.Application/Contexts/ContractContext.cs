using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Contexts
{
    public class ContractContext : BaseContext
    {
        private readonly IContractGateway _contractGateway;
        private readonly IUserGateway _userGateway;
        private readonly IManagerDelegationGateway _delegationGateway;
        private readonly NotificationContext _notifications;
        private readonly DelegationGuard _delegationGuard;

        public ContractContext(
            ILogger<ContractContext> logger,
            IContractGateway contractGateway,
            IUserGateway userGateway,
            IManagerDelegationGateway delegationGateway,
            NotificationContext notifications,
            DelegationGuard delegationGuard) : base(logger)
        {
            _contractGateway = contractGateway;
            _userGateway = userGateway;
            _delegationGateway = delegationGateway;
            _notifications = notifications;
            _delegationGuard = delegationGuard;
        }

        private Task<ManagerDelegation?> GuardAsync(Guid actingAsUserId, ActingOnBehalf? onBehalf) =>
            _delegationGuard.GuardAsync(actingAsUserId, onBehalf);

        private Task RecordDelegatedActionAsync(
            ManagerDelegation? delegation, Guid actingAsUserId, Guid targetUserId,
            DelegatedActionType actionType, Guid targetEntityId, string? details) =>
            _delegationGuard.RecordDelegatedActionAsync(delegation, actingAsUserId, targetUserId, actionType, targetEntityId, details);

        private static string ActorLabel(User actingAs, ManagerDelegation? delegation) =>
            DelegationGuard.ActorLabel(actingAs, delegation);

        private async Task<User> GetUserOrThrowAsync(Guid userId) =>
            await _userGateway.GetUserByIdAsync(userId)
            ?? throw new EntityNotFoundException($"No user with id {userId}.");

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

        public async Task ExtendContractAsync(
            Guid managerId, Guid contractId, DateOnly newEndDate, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(managerId, onBehalf);
            var manager = await GetUserOrThrowAsync(managerId);
            var contract = await _contractGateway.GetByIdAsync(contractId);
            if (contract == null)
                throw new EntityNotFoundException($"Contract with ID {contractId} not found.");
            if (contract.User.RegionId != manager.RegionId)
                throw new UnauthorizedException("You cannot manage contracts from another region.");

            // Check authorization: direct manager or active delegate
            var today = DateOnly.FromDateTime(DateTime.Today);
            var isDirectManager = contract.User.ManagerId == managerId;
            var isAuthorizedDelegate = false;

            if (!isDirectManager && contract.User.ManagerId.HasValue)
            {
                var delegatedManagerIds = await _delegationGateway.GetDelegatedManagerIdsAsync(managerId, today);
                isAuthorizedDelegate = delegatedManagerIds.Contains(contract.User.ManagerId.Value);
            }

            if (!isDirectManager && !isAuthorizedDelegate)
                throw new UnauthorizedException("You are not authorized to manage this contract.");

            if (contract.Type != ContractType.Determinate)
                throw new InvalidOperationException("Only determinate (fixed-term) contracts can have their end date extended.");

            if (contract.Status != ContractStatus.Active)
                throw new InvalidOperationException("Only active contracts can be extended.");

            if (newEndDate <= contract.StartDate)
                throw new InvalidOperationException("New end date must be after the contract start date.");

            if (contract.EndDate.HasValue && newEndDate <= contract.EndDate.Value)
                throw new InvalidOperationException("New end date must be strictly after the current end date.");

            var previousEnd = contract.EndDate?.ToString("yyyy-MM-dd") ?? "none";
            contract.EndDate = newEndDate;
            contract.UpdatedAt = DateTime.UtcNow;

            await _contractGateway.UpdateAsync(contract);

            await RecordDelegatedActionAsync(
                delegation, managerId, contract.UserId, DelegatedActionType.ContractExtended,
                contract.Id, $"End date {previousEnd} → {newEndDate:yyyy-MM-dd}");

            try
            {
                await _notifications.SendNotificationAsync(
                    contract.UserId,
                    $"Your employment contract has been extended to {newEndDate:yyyy-MM-dd} by {ActorLabel(manager, delegation)}.",
                    "/employee/profile");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Contract extension for {ContractId} succeeded but notification failed.", contractId);
            }

            _logger.LogInformation("Manager {ManagerId} extended contract {ContractId} (User {UserId}) from {PreviousEnd} to {NewEnd}.",
                managerId, contractId, contract.UserId, previousEnd, newEndDate);
        }

        public async Task TerminateContractAsync(
            Guid managerId, Guid contractId, string? reason, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await GuardAsync(managerId, onBehalf);
            var manager = await GetUserOrThrowAsync(managerId);
            var contract = await _contractGateway.GetByIdAsync(contractId);
            if (contract == null)
                throw new EntityNotFoundException($"Contract with ID {contractId} not found.");
            if (contract.User.RegionId != manager.RegionId)
                throw new UnauthorizedException("You cannot manage contracts from another region.");

            var today = DateOnly.FromDateTime(DateTime.Today);
            var isDirectManager = contract.User.ManagerId == managerId;
            var isAuthorizedDelegate = false;

            if (!isDirectManager && contract.User.ManagerId.HasValue)
            {
                var delegatedManagerIds = await _delegationGateway.GetDelegatedManagerIdsAsync(managerId, today);
                isAuthorizedDelegate = delegatedManagerIds.Contains(contract.User.ManagerId.Value);
            }

            if (!isDirectManager && !isAuthorizedDelegate)
                throw new UnauthorizedException("You are not authorized to manage this contract.");

            if (contract.Status == ContractStatus.Terminated)
                throw new InvalidOperationException("Contract is already terminated.");

            contract.Status = ContractStatus.Terminated;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                contract.Notes = string.IsNullOrWhiteSpace(contract.Notes)
                    ? $"[Terminated: {reason}]"
                    : $"{contract.Notes} | [Terminated: {reason}]";
            }
            contract.UpdatedAt = DateTime.UtcNow;

            await _contractGateway.UpdateAsync(contract);

            await RecordDelegatedActionAsync(
                delegation, managerId, contract.UserId, DelegatedActionType.ContractTerminated,
                contract.Id, reason);

            try
            {
                await _notifications.SendNotificationAsync(
                    contract.UserId,
                    $"Your employment contract has been terminated by {ActorLabel(manager, delegation)}. Reason: {reason ?? "No reason specified"}.",
                    "/employee/profile");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Contract termination for {ContractId} succeeded but notification failed.", contractId);
            }

            _logger.LogInformation("Manager {ManagerId} terminated contract {ContractId} (User {UserId}). Reason: {Reason}",
                managerId, contractId, contract.UserId, reason);
        }
    }
}
