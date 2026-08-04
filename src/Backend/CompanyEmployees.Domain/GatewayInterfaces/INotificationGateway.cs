using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface INotificationGateway
    {
        Task<Notification> CreateNotificationAsync(Notification notification);
        Task<List<Notification>> GetUnreadNotificationsAsync(Guid userId);
        Task MarkAsReadAsync(Guid notificationId);

        // Read and unread alike, newest first — the bell shows history, not just a to-do
        // list, so an already-seen notification has to stay visible.
        Task<List<Notification>> GetRecentAsync(Guid userId, int take);

        // Same ordering, but windowed: the history page can't load a year of rows at once.
        Task<List<Notification>> GetPageAsync(Guid userId, int skip, int take);
        Task<int> CountAsync(Guid userId);

        // The badge only needs a number. Counting in SQL avoids materializing rows the
        // UI never renders.
        Task<int> GetUnreadCountAsync(Guid userId);

        Task MarkAllAsReadAsync(Guid userId);
    }
}
