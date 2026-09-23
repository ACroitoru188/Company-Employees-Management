using CompanyEmployees.Application.Messaging;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
// The domain defines its own InvalidOperationException; the alias picks it over System's.
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Contexts
{
    // Team chat. The circle is the Team page's roster (your manager plus the colleagues who
    // share them) *plus your own direct reports*.
    //
    // Those reports are the whole reason this is not simply GetTeamMembersAsync: that relation
    // is not symmetric. Your manager is on your roster, but you are not on theirs unless you
    // happen to share a manager — so a line manager could be written to and could not reply.
    // Adding direct reports makes the relation symmetric by construction: if Y is on X's
    // roster as their manager, X is among Y's reports, and every peer pair is mutual already.
    //
    // GetTeamMembersAsync itself is deliberately left alone. It is the single definition of
    // team *visibility* for the calendar, the dashboard and the roster, and widening it would
    // quietly change all three.
    public class MessagingContext : BaseContext
    {
        public const int MaxBodyLength = 2000;

        private readonly IMessageGateway _messageGateway;
        private readonly IUserGateway _userGateway;
        private readonly LeaveContext _team;
        private readonly IMessageDispatcher _dispatcher;
        private readonly DelegationGuard _delegationGuard;

        public MessagingContext(
            ILogger<MessagingContext> logger,
            IMessageGateway messageGateway,
            IUserGateway userGateway,
            LeaveContext team,
            IMessageDispatcher dispatcher,
            DelegationGuard delegationGuard) : base(logger)
        {
            _messageGateway = messageGateway;
            _userGateway = userGateway;
            _team = team;
            _dispatcher = dispatcher;
            _delegationGuard = delegationGuard;
        }

        public async Task<ChatMessage> SendMessageAsync(
            Guid senderId, Guid recipientId, string body, ActingOnBehalf? onBehalf = null)
        {
            var delegation = await _delegationGuard.GuardAsync(senderId, onBehalf);

            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("A message cannot be empty.");

            var trimmed = body.Trim();
            if (trimmed.Length > MaxBodyLength)
                throw new InvalidOperationException(
                    $"A message cannot be longer than {MaxBodyLength} characters.");

            var recipient = await EnsureTeammateAsync(senderId, recipientId);

            var message = new ChatMessage
            {
                Id = Guid.NewGuid(),
                SenderId = senderId,
                RecipientId = recipient.Id,
                Body = trimmed,
                CreatedAt = DateTime.UtcNow
            };

            await _messageGateway.AddAsync(message);

            await _delegationGuard.RecordDelegatedActionAsync(
                delegation, senderId, recipient.Id,
                DelegatedActionType.MessageSent, message.Id, null);

            // Best effort, like the notification paths: the message is saved either way, and
            // a recipient with nothing open simply sees it when they open the thread.
            try
            {
                await _dispatcher.PublishCreatedAsync(recipient.Id, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Message {MessageId} saved but live delivery failed.", message.Id);
            }

            _logger.LogInformation(
                "User {SenderId} messaged {RecipientId} ({Length} chars).",
                senderId, recipient.Id, trimmed.Length);

            return message;
        }

        // Oldest-first, which is the order a thread is read in. The gateway pages from the
        // newest end because that is the cheap end to page from; the reversal happens here.
        public async Task<List<ChatMessage>> GetConversationAsync(
            Guid userId, Guid otherUserId, int take = 50, DateTime? before = null)
        {
            await EnsureTeammateAsync(userId, otherUserId);

            var page = await _messageGateway.GetConversationAsync(userId, otherUserId, take, before);
            page.Reverse();
            return page;
        }

        // Everyone this user may exchange messages with. The panel lists exactly this, so the
        // list and the guard below can never disagree — seeing somebody you cannot write to is
        // the same bug from the other side.
        public async Task<List<User>> GetChatContactsAsync(Guid userId)
        {
            var me = await _userGateway.GetUserByIdAsync(userId);
            if (me == null)
                throw new EntityNotFoundException($"No user with id {userId}.");

            var contacts = await _team.GetTeamMembersAsync(userId);
            var seen = contacts.Select(contact => contact.Id).ToHashSet();

            // Region-scoped like the roster: acting stays regional even though looking does not.
            foreach (var report in await _userGateway.GetDirectReportsAsync(userId))
            {
                if (report.Id != userId
                    && report.Status == UserStatus.Active
                    && report.RegionId == me.RegionId
                    && seen.Add(report.Id))
                    contacts.Add(report);
            }

            return contacts;
        }

        public Task<List<ConversationSummary>> GetInboxAsync(Guid userId) =>
            _messageGateway.GetInboxAsync(userId);

        public Task<int> GetUnreadCountAsync(Guid userId) =>
            _messageGateway.GetUnreadCountAsync(userId);

        public async Task MarkConversationReadAsync(Guid userId, Guid otherUserId)
        {
            // No team check: someone who has left your team must still be able to clear the
            // messages they already sent you, or the badge would never go away.
            await _messageGateway.MarkConversationReadAsync(userId, otherUserId);

            // Tells this user's own other components — the drawer badge above all — to re-read.
            // Best effort: the rows are already marked, a stale badge is not worth an exception.
            try
            {
                await _dispatcher.PublishReadStateChangedAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Read state for {UserId} saved but the signal failed.", userId);
            }
        }

        // The whole access rule, in one place and applied to reads as well as writes — the
        // page hides people you may not message, but the route takes an id from the URL.
        private async Task<User> EnsureTeammateAsync(Guid userId, Guid otherUserId)
        {
            if (userId == otherUserId)
                throw new InvalidOperationException("You cannot message yourself.");

            var other = await _userGateway.GetUserByIdAsync(otherUserId);
            if (other == null)
                throw new EntityNotFoundException($"No user with id {otherUserId}.");

            var contacts = await GetChatContactsAsync(userId);
            if (contacts.All(contact => contact.Id != otherUserId))
                throw new UnauthorizedException("You can only message your own team.");

            return other;
        }
    }
}
