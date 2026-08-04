using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface INotificationGateway
    {
        Task<Notification> CreateNotificationAsync(Notification notification);

        // Scoped to the owner: an id alone must not be enough to flip someone else's row.
        // A foreign id is a no-op, not an error.
        Task MarkAsReadAsync(Guid userId, Guid notificationId);

        Task MarkAllAsReadAsync(Guid userId);

        // Read and unread alike — the bell is a history, not a to-do list.
        Task<List<Notification>> GetRecentAsync(Guid userId, int take);

        Task<List<Notification>> GetPageAsync(Guid userId, int skip, int take);

        Task<int> CountAsync(Guid userId);

        Task<int> GetUnreadCountAsync(Guid userId);
    }
}
