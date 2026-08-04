using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;

namespace CompanyEmployees.Application.Contexts
{
    public class NotificationContext
    {
        private readonly INotificationGateway _gateway;
        private readonly INotificationDispatcher _dispatcher;

        public NotificationContext(INotificationGateway gateway, INotificationDispatcher dispatcher)
        {
            _gateway = gateway;
            _dispatcher = dispatcher;
        }

        public async Task<Notification> SendNotificationAsync(Guid userId, string message, string? actionUrl = null)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                ActionUrl = actionUrl
            };

            await _gateway.CreateNotificationAsync(notification);
            await _dispatcher.PublishAsync(userId, notification);
            return notification;
        }

        // The bell shows a short history; the count comes separately so the badge
        // stays accurate even when the list is trimmed to the newest few.
        public Task<List<Notification>> GetRecentAsync(Guid userId, int take = 8) =>
            _gateway.GetRecentAsync(userId, take);

        public Task<int> GetUnreadCountAsync(Guid userId) =>
            _gateway.GetUnreadCountAsync(userId);

        // Paged history for /employee/notifications.
        public Task<List<Notification>> GetHistoryPageAsync(Guid userId, int skip, int take) =>
            _gateway.GetPageAsync(userId, skip, take);

        public Task<int> GetHistoryCountAsync(Guid userId) =>
            _gateway.CountAsync(userId);

        public Task MarkAsReadAsync(Guid notificationId) =>
            _gateway.MarkAsReadAsync(notificationId);

        public Task MarkAllAsReadAsync(Guid userId) =>
            _gateway.MarkAllAsReadAsync(userId);
    }
}
