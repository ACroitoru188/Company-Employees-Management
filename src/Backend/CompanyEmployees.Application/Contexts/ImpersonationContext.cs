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
        public async Task<User> StartAsync(Guid realUserId, Guid delegationId, string? ipAddress)
        {
            // No chaining: an already-borrowed account must not be able to borrow another.
            var openSession = await _sessions.GetOpenSessionAsync(realUserId);
            if (openSession != null)
                throw new InvalidOperationException("You are already acting as someone else. Return to your own account first.");

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
            var openSession = await _sessions.GetOpenSessionAsync(realUserId);
            if (openSession != null)
                await _sessions.EndSessionAsync(openSession.Id, DateTime.UtcNow);

            var realUser = await _users.GetUserByIdAsync(realUserId)
                ?? throw new EntityNotFoundException($"No user with id {realUserId}.");

            _logger.LogInformation("User {RealUserId} returned to their own account.", realUserId);
            return realUser;
        }

        // Re-checked before every impersonated action, not only when switching: the cookie
        // outlives the delegation, so a cancelled or expired window has to bite immediately.
        public async Task<ManagerDelegation> ValidateDelegationAsync(Guid realUserId, Guid delegationId)
        {
            var delegation = await _delegations.GetByIdAsync(delegationId)
                ?? throw new EntityNotFoundException($"No delegation with id {delegationId}.");

            if (delegation.DelegateId != realUserId)
                throw new UnauthorizedException("This delegation was not given to you.");

            if (delegation.ManagerId == realUserId)
                throw new InvalidOperationException("You cannot act as yourself.");

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!delegation.IsActive || delegation.StartDate > today || delegation.EndDate < today)
                throw new UnauthorizedException("This delegation is no longer active.");

            return delegation;
        }

        // What the profile switcher offers.
        public Task<List<ManagerDelegation>> GetAvailableDelegationsAsync(Guid realUserId) =>
            _delegations.GetActiveDelegationsForDelegateAsync(realUserId, DateOnly.FromDateTime(DateTime.Today));
    }
}
