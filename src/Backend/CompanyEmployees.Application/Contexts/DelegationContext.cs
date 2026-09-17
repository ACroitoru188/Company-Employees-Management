using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Contexts
{
    public class DelegationContext : BaseContext
    {
        private readonly IManagerDelegationGateway _delegationGateway;
        private readonly IUserGateway _userGateway;
        private readonly IDelegatedActionGateway _delegatedActions;
        private readonly NotificationContext _notifications;

        public DelegationContext(
            ILogger<DelegationContext> logger,
            IManagerDelegationGateway delegationGateway,
            IUserGateway userGateway,
            IDelegatedActionGateway delegatedActions,
            NotificationContext notifications) : base(logger)
        {
            _delegationGateway = delegationGateway;
            _userGateway = userGateway;
            _delegatedActions = delegatedActions;
            _notifications = notifications;
        }

        public async Task<List<User>> GetEligibleDelegatesAsync(Guid userId)
        {
            var caller = await GetUserOrThrowAsync(userId);
            return (await _userGateway.GetAllUsersAsync())
                .Where(user => user.RegionId == caller.RegionId && user.Id != userId && user.Status == UserStatus.Active)
                .ToList();
        }

        public async Task<ManagerDelegation> CreateDelegationAsync(
            Guid delegatorId, Guid delegateId, DateOnly start, DateOnly end, string? reason,
            ActingOnBehalf? onBehalf = null)
        {
            // No chaining: authority that was lent cannot be lent onward. Only the account's
            // real owner may hand it to someone else.
            if (onBehalf is not null)
                throw new UnauthorizedException("You cannot delegate from an account you are only borrowing.");

            if (delegatorId == delegateId)
                throw new InvalidOperationException("You cannot delegate responsibilities to yourself.");

            if (start > end)
                throw new InvalidOperationException("End date cannot be earlier than start date.");

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (end < today)
                throw new InvalidOperationException("Cannot create a delegation for a period that has already ended.");

            var delegateUser = await _userGateway.GetUserByIdAsync(delegateId);
            if (delegateUser == null || delegateUser.Status != UserStatus.Active)
                throw new EntityNotFoundException("Selected delegate user was not found or is inactive.");

            var delegator = await GetUserOrThrowAsync(delegatorId);
            if (delegateUser.RegionId != delegator.RegionId)
                throw new UnauthorizedException("You can only delegate to a manager in your region.");

            // One stand-in at a time: a second delegation overlapping an existing one leaves
            // two people acting for the same account on the same day.
            if (await _delegationGateway.HasActiveDelegationInPeriodAsync(delegatorId, start, end))
                throw new InvalidOperationException(
                    "You already have a delegation covering part of this period. Cancel it first or choose different dates.");

            var delegation = new ManagerDelegation
            {
                Id = Guid.NewGuid(),
                ManagerId = delegatorId,
                DelegateId = delegateId,
                StartDate = start,
                EndDate = end,
                Reason = reason,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _delegationGateway.CreateAsync(delegation);

            try
            {
                var period = start.ToString("MMM d", CultureInfo.InvariantCulture)
                             + " – " +
                             end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

                var message = delegator.Role == UserRole.LineManager
                    ? $"You have been assigned as temporary Line Manager delegate for {delegator.Name}, {period}."
                    : $"{delegator.Name} asked you to cover for them, {period}.";

                await _notifications.SendNotificationAsync(delegateId, message, "/employee/delegations");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Delegation creation succeeded but notification failed.");
            }

            _logger.LogInformation("User {DelegatorId} created delegation to {DelegateId} from {Start} to {End}.",
                delegatorId, delegateId, start, end);

            return delegation;
        }

        public async Task CancelDelegationAsync(Guid delegatorId, Guid delegationId)
        {
            var delegation = await _delegationGateway.GetByIdAsync(delegationId);
            if (delegation == null)
                throw new EntityNotFoundException($"Delegation with ID {delegationId} not found.");

            if (delegation.ManagerId != delegatorId)
                throw new UnauthorizedException("You are not authorized to cancel this delegation.");

            delegation.IsActive = false;
            await _delegationGateway.UpdateAsync(delegation);

            _logger.LogInformation("User {DelegatorId} cancelled delegation {DelegationId}.", delegatorId, delegationId);
        }

        public async Task<DelegationHistoryResult> GetDelegationHistoryAsync(
            Guid userId, DelegationHistoryScope scope, int skip, int take)
        {
            List<DelegatedAction> actions;
            int total;
            string? regionName = null;

            if (scope == DelegationHistoryScope.EveryoneInRegion)
            {
                var caller = await GetUserOrThrowAsync(userId);
                if (caller.Role != UserRole.Admin)
                    throw new UnauthorizedException("Only an administrator can view the whole region's history.");

                actions = await _delegatedActions.GetForRegionAsync(caller.RegionId, skip, take);
                total = await _delegatedActions.CountForRegionAsync(caller.RegionId);
                regionName = caller.Region?.Name;
            }
            else if (scope == DelegationHistoryScope.DoneInMyName)
            {
                actions = await _delegatedActions.GetActedAsAsync(userId, skip, take);
                total = await _delegatedActions.CountActedAsAsync(userId);
            }
            else
            {
                actions = await _delegatedActions.GetPerformedByAsync(userId, skip, take);
                total = await _delegatedActions.CountPerformedByAsync(userId);
            }

            return new DelegationHistoryResult
            {
                Total = total,
                RegionName = regionName,
                Items = actions.Select(action => new DelegationHistoryEntry
                {
                    When = action.CreatedAt,
                    RealUserName = action.RealUser.Name,
                    ActedAsName = action.ActedAsUser.Name,
                    TargetName = action.TargetUser.Name,
                    ActionType = action.ActionType,
                    Details = action.Details
                }).ToList()
            };
        }

        public async Task<List<ManagerDelegation>> GetMyDelegationsAsync(Guid delegatorId)
        {
            var delegator = await GetUserOrThrowAsync(delegatorId);
            return (await _delegationGateway.GetAllDelegationsByManagerAsync(delegatorId))
                .Where(delegation => delegation.Delegate.RegionId == delegator.RegionId)
                .ToList();
        }

        public async Task<List<ManagerDelegation>> GetActiveDelegationsAssignedToMeAsync(Guid delegateId)
        {
            var delegateUser = await GetUserOrThrowAsync(delegateId);
            var today = DateOnly.FromDateTime(DateTime.Today);
            return (await _delegationGateway.GetActiveDelegationsForDelegateAsync(delegateId, today))
                .Where(delegation => delegation.Manager.RegionId == delegateUser.RegionId)
                .ToList();
        }

        private async Task<User> GetUserOrThrowAsync(Guid userId)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            return user ?? throw new EntityNotFoundException($"No user with id {userId}.");
        }
    }
}
