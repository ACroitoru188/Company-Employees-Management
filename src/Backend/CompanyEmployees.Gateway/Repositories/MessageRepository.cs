using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class MessageRepository : BaseRepository, IMessageGateway
    {
        public MessageRepository(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task AddAsync(ChatMessage message)
        {
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();
        }

        public async Task<List<ChatMessage>> GetConversationAsync(
            Guid userId, Guid otherUserId, int take, DateTime? before = null)
        {
            // Either direction: the pair is the conversation, so a message counts whichever
            // way it went.
            var query = _context.Messages
                .Where(m => (m.SenderId == userId && m.RecipientId == otherUserId)
                         || (m.SenderId == otherUserId && m.RecipientId == userId));

            if (before is not null)
                query = query.Where(m => m.CreatedAt < before);

            // Newest first so "load older" is a cheap Take from the same end; the caller
            // reverses the page for display.
            return await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(take)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<ConversationSummary>> GetInboxAsync(Guid userId)
        {
            // Everyone this user has exchanged anything with, plus the other party loaded so
            // the list can show a name without a second round trip per row.
            var messages = await _context.Messages
                .Where(m => m.SenderId == userId || m.RecipientId == userId)
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .AsNoTracking()
                .ToListAsync();

            var summaries = new List<ConversationSummary>();

            foreach (var group in messages.GroupBy(m => m.SenderId == userId ? m.RecipientId : m.SenderId))
            {
                var last = group.OrderByDescending(m => m.CreatedAt).First();

                // Only their messages can be unread by me; my own never count.
                var unread = group.Count(m => m.RecipientId == userId && m.ReadAt == null);

                var other = last.SenderId == userId ? last.Recipient : last.Sender;
                summaries.Add(new ConversationSummary(other, last, unread));
            }

            // Most recently active first, which is the order a chat list is read in.
            summaries.Sort((a, b) => b.LastMessage.CreatedAt.CompareTo(a.LastMessage.CreatedAt));
            return summaries;
        }

        public async Task MarkConversationReadAsync(Guid userId, Guid otherUserId)
        {
            // Only what the other party sent to this user: scoped to the owner, so an id alone
            // cannot clear somebody else's unread count.
            var unread = await _context.Messages
                .Where(m => m.RecipientId == userId
                         && m.SenderId == otherUserId
                         && m.ReadAt == null)
                .ToListAsync();

            if (unread.Count == 0)
                return;

            var now = DateTime.UtcNow;
            foreach (var message in unread)
                message.ReadAt = now;

            await _context.SaveChangesAsync();
        }

        public Task<int> GetUnreadCountAsync(Guid userId) =>
            _context.Messages.CountAsync(m => m.RecipientId == userId && m.ReadAt == null);
    }
}
