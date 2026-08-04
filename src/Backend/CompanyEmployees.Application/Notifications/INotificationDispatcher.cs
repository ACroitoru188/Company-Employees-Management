using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Application.Notifications
{
    // In-process fan-out from the code that creates a notification to whatever UI is
    // currently showing that user's bell. Lives here rather than in a SignalR hub because
    // Blazor Server already owns a live connection to every open tab — a second hop over
    // the network would only add a publicly reachable endpoint to secure.
    public interface INotificationDispatcher
    {
        // Dispose the returned handle to stop listening. The caller owns it: a component
        // that forgets leaks its handler for the lifetime of the process.
        IDisposable Subscribe(Guid userId, Func<Notification, Task> handler);

        Task PublishAsync(Guid userId, Notification notification);
    }
}
