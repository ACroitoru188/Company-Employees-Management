using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Contexts
{
    // Borrowing an account is the most powerful thing a non-admin can do here, so every rule
    // lives in this one class. The endpoints only translate its result into a redirect.
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

        // Returns the account to sign in as. Throws when the switch is not allowed.
        //
        // Chaining is refused by the caller, from the cookie: a row left open by a sign-out
        // or an expired cookie says nothing about whether an account is being borrowed right
        // now, and treating it as if it did locked people out permanently. Any such row is
        // closed here instead.
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

        // Also called on sign-out: leaving the row open there is what used to make the next
        // switch impossible.
        public async Task EndOpenSessionAsync(Guid realUserId)
        {
            var openSession = await _sessions.GetOpenSessionAsync(realUserId);
            if (openSession != null)
                await _sessions.EndSessionAsync(openSession.Id, DateTime.UtcNow);
        }

        // Re-checked before every impersonated action, not only when switching: the auth
        // cookie lasts five hours and outlives the delegation, so a cancelled or expired
        // window has to bite the moment it changes rather than at the next sign-in.
        public async Task<ManagerDelegation> ValidateDelegationAsync(
            Guid realUserId, Guid delegationId, Guid? actingAsUserId = null)
        {
            var delegation = await _delegations.GetByIdAsync(delegationId)
                ?? throw new EntityNotFoundException($"No delegation with id {delegationId}.");

            if (delegation.DelegateId != realUserId)
                throw new UnauthorizedException("This delegation was not given to you.");

            // The cookie belongs to the borrowed account, so Identity revalidates that
            // account's security stamp and never the delegate's. Deactivating the delegate
            // has to end their borrowed access too, hence checking them here.
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
