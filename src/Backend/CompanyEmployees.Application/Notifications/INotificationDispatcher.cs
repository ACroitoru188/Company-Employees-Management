using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Application.Notifications
{
    // In-process fan-out to the UI currently showing a user's notifications. Not a SignalR
    // hub: Blazor Server already holds a connection to every open tab, so a hub would only
    // add a publicly reachable endpoint to secure.
    public interface INotificationDispatcher
    {
        IDisposable Subscribe(Guid userId, Func<NotificationChange, Task> handler);

        // Both hand off without waiting for subscribers: a handler ends in a Blazor render,
        // and the caller is the request that just saved the change.
        Task PublishCreatedAsync(Guid userId, Notification notification);

        Task PublishReadStateChangedAsync(Guid userId);
    }
}
