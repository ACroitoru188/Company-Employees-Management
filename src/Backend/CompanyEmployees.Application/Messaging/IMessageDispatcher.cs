using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Application.Messaging
{
    // In-process fan-out to whoever currently has a chat open, keyed by the user the change is
    // *for*. Same shape and the same reasoning as INotificationDispatcher: Blazor Server
    // already holds a connection to every open tab, so a hub would only add a publicly
    // reachable endpoint to secure.
    //
    // Kept separate from the notification dispatcher on purpose — routing chat through it
    // would mean a Notification row per message, and the bell would fill up with chatter.
    public interface IMessageDispatcher
    {
        IDisposable Subscribe(Guid userId, Func<MessageChange, Task> handler);

        // Hands off without waiting for subscribers: a handler ends in a Blazor render, and
        // sending must not wait on the recipient's browser.
        Task PublishCreatedAsync(Guid recipientId, ChatMessage message);

        // Opening a thread clears its unread rows, and the drawer's badge is rendered by a
        // different component on a different page. Without this signal it keeps the count it
        // last read, which is the moment before the thread was opened.
        Task PublishReadStateChangedAsync(Guid userId);
    }

    // Message is null when only read state moved, which subscribers answer by re-reading.
    // Mirrors NotificationChange for the same reason: one subscription, two kinds of news.
    public sealed record MessageChange(ChatMessage? Message)
    {
        public static MessageChange ForCreated(ChatMessage message) => new(message);

        public static MessageChange ReadStateChanged { get; } = new((ChatMessage?)null);
    }
}
