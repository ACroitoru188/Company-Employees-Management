using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.GatewayInterfaces;

namespace CompanyEmployees.Application.Contexts
{
    public sealed class DelegationGuard
    {
        private readonly ImpersonationContext _impersonation;
        private readonly IDelegatedActionGateway _delegatedActions;

        public DelegationGuard(
            ImpersonationContext impersonation,
            IDelegatedActionGateway delegatedActions)
        {
            _impersonation = impersonation;
            _delegatedActions = delegatedActions;
        }

        // Null means the caller is acting as themselves.
        public async Task<ManagerDelegation?> GuardAsync(Guid actingAsUserId, ActingOnBehalf? onBehalf)
        {
            if (onBehalf is null)
                return null;

            return await _impersonation.ValidateDelegationAsync(
                onBehalf.RealUserId, onBehalf.DelegationId, actingAsUserId);
        }

        public static string ActorLabel(User actingAs, ManagerDelegation? delegation)
        {
            var who = actingAs.Role switch
            {
                UserRole.LineManager => $"Line Manager {actingAs.Name}",
                UserRole.Admin => $"Administrator {actingAs.Name}",
                _ => actingAs.Name
            };

            return delegation is null ? who : $"{who} (delegate: {delegation.Delegate.Name})";
        }

        // No-op when delegation is null (direct action, nothing to audit).
        public Task RecordDelegatedActionAsync(
            ManagerDelegation? delegation, Guid actingAsUserId, Guid targetUserId,
            DelegatedActionType actionType, Guid targetEntityId, string? details)
        {
            if (delegation is null)
                return Task.CompletedTask;

            return _delegatedActions.CreateAsync(new DelegatedAction
            {
                Id = Guid.NewGuid(),
                DelegationId = delegation.Id,
                RealUserId = delegation.DelegateId,
                ActedAsUserId = actingAsUserId,
                TargetUserId = targetUserId,
                ActionType = actionType,
                TargetEntityId = targetEntityId,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
