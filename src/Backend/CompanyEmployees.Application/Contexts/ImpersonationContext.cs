using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Contexts
{
    // Coordinates impersonation sessions and delegation validation rules.
    public class ImpersonationContext : BaseContext
    {
        private readonly IImpersonationGateway _sessions;
        private readonly IManagerDelegationGateway _delegations;
        private readonly IUserGateway _users;

        public ImpersonationContext(
            ILogger<ImpersonationContext> logger,
            IImpersonationGateway sessions,
            IManagerDelegationGateway delegations,
            IUserGateway users) : base(logger)
        {
            _sessions = sessions;
            _delegations = delegations;
            _users = users;
        }

        // Starts an impersonation session and returns the target user account.
        public async Task<User> StartAsync(Guid realUserId, Guid delegationId, string? ipAddress)
        {
            await EndOpenSessionAsync(realUserId);

            var delegation = await ValidateDelegationAsync(realUserId, delegationId);

            var target = await _users.GetUserByIdAsync(delegation.ManagerId)
                ?? throw new EntityNotFoundException($"No user with id {delegation.ManagerId}.");
            if (target.Status != UserStatus.Active)
                throw new InvalidOperationException("That account is inactive.");

            await _sessions.CreateAsync(new ImpersonationSession
            {
                Id = Guid.NewGuid(),
                DelegationId = delegation.Id,
                RealUserId = realUserId,
                ActedAsUserId = target.Id,
                StartedAt = DateTime.UtcNow,
                IpAddress = ipAddress
            });

            _logger.LogInformation("User {RealUserId} started acting as {TargetId} under delegation {DelegationId}.",
                realUserId, target.Id, delegation.Id);

            return target;
        }

        // Returns the account to sign back in as.
        public async Task<User> StopAsync(Guid realUserId)
        {
            await EndOpenSessionAsync(realUserId);

            var realUser = await _users.GetUserByIdAsync(realUserId)
                ?? throw new EntityNotFoundException($"No user with id {realUserId}.");

            _logger.LogInformation("User {RealUserId} returned to their own account.", realUserId);
            return realUser;
        }

        // Ends any open impersonation session for the given user.
        public async Task EndOpenSessionAsync(Guid realUserId)
        {
            var openSession = await _sessions.GetOpenSessionAsync(realUserId);
            if (openSession != null)
                await _sessions.EndSessionAsync(openSession.Id, DateTime.UtcNow);
        }

        // Re-validates delegation validity before each delegated action.
        public async Task<ManagerDelegation> ValidateDelegationAsync(
            Guid realUserId, Guid delegationId, Guid? actingAsUserId = null)
        {
            var delegation = await _delegations.GetByIdAsync(delegationId)
                ?? throw new EntityNotFoundException($"No delegation with id {delegationId}.");

            if (delegation.DelegateId != realUserId)
                throw new UnauthorizedException("This delegation was not given to you.");

            // Ensure the delegate's own account remains active.
            var realUser = await _users.GetUserByIdAsync(realUserId);
            if (realUser is null || realUser.Status != UserStatus.Active)
                throw new UnauthorizedException("Your own account is no longer active.");

            if (delegation.ManagerId == realUserId)
                throw new InvalidOperationException("You cannot act as yourself.");

            // Guards against a stale cookie pointing at a delegation for a different account.
            if (actingAsUserId.HasValue && delegation.ManagerId != actingAsUserId.Value)
                throw new UnauthorizedException("This delegation does not cover that account.");

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!delegation.IsActive || delegation.StartDate > today || delegation.EndDate < today)
                throw new UnauthorizedException("This delegation is no longer active.");

            return delegation;
        }

        // What the profile switcher offers.
        public Task<List<ManagerDelegation>> GetAvailableDelegationsAsync(Guid realUserId) =>
            _delegations.GetActiveDelegationsForDelegateAsync(realUserId, DateOnly.FromDateTime(DateTime.Today));

        // Whether the delegation history is worth a nav entry: admins always have it as
        // oversight, everyone else only once they have delegated or been delegated to.
        public async Task<bool> CanSeeDelegationHistoryAsync(Guid userId)
        {
            var user = await _users.GetUserByIdAsync(userId);
            return user?.Role == UserRole.Admin
                   || await _delegations.HasAnyDelegationAsync(userId);
        }
    }
}
