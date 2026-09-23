namespace CompanyEmployees.Domain.GatewayInterfaces;

using CompanyEmployees.Domain.Entities;

// One 1:1 conversation is the pair of users, so every read here takes both ids and matches
// messages in either direction rather than looking up a thread row.
public interface IMessageGateway
{
    Task AddAsync(ChatMessage message);

    // Newest-first page of the conversation between two people. The page is reversed for
    // display by the caller; taking from the newest end is what makes paging back cheap.
    Task<List<ChatMessage>> GetConversationAsync(Guid userId, Guid otherUserId, int take, DateTime? before = null);

    // One entry per person this user has exchanged messages with: the other party, the last
    // message, and how many of theirs this user has not opened yet.
    Task<List<ConversationSummary>> GetInboxAsync(Guid userId);

    // Marks everything the other party sent to this user as read. Scoped to the owner: an id
    // alone must not be enough to clear somebody else's unread count.
    Task MarkConversationReadAsync(Guid userId, Guid otherUserId);

    Task<int> GetUnreadCountAsync(Guid userId);
}

// Projection, not an entity: there is no conversation row to map back to.
public record ConversationSummary(User Other, ChatMessage LastMessage, int UnreadCount);
