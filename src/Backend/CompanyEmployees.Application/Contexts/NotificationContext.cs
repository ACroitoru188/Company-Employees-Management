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
            await _dispatcher.PublishCreatedAsync(userId, notification);
            return notification;
        }

        public Task<List<Notification>> GetRecentAsync(Guid userId, int take = 8) =>
            _gateway.GetRecentAsync(userId, take);

        public Task<int> GetUnreadCountAsync(Guid userId) =>
            _gateway.GetUnreadCountAsync(userId);

        public Task<List<Notification>> GetHistoryPageAsync(Guid userId, int skip, int take) =>
            _gateway.GetPageAsync(userId, skip, take);

        public Task<int> GetHistoryCountAsync(Guid userId) =>
            _gateway.CountAsync(userId);

        // Both publish so the bell re-reads: it lives in the layout and survives navigation,
        // so nothing else would tell it the history page marked something read.
        public async Task MarkAsReadAsync(Guid userId, Guid notificationId)
        {
            await _gateway.MarkAsReadAsync(userId, notificationId);
            await _dispatcher.PublishReadStateChangedAsync(userId);
        }

        public async Task MarkAllAsReadAsync(Guid userId)
        {
            await _gateway.MarkAllAsReadAsync(userId);
            await _dispatcher.PublishReadStateChangedAsync(userId);
        }
    }
}
