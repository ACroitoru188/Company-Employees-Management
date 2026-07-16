using CompanyEmployees.Application.Hubs;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.AspNetCore.SignalR;

namespace CompanyEmployees.Application.Contexts
{
    public class NotificationContext
    {
        private readonly INotificationGateway _gateway;

        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationContext(INotificationGateway gateway, IHubContext<NotificationHub> hubContext)
        {
            _gateway = gateway;
            _hubContext = hubContext;
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

            await _hubContext.Clients.User(userId.ToString())
                .SendAsync("ReceiveNotification", notification);
            return notification;
        }

        public async Task<List<Notification>> GetMyUnreadNotificationsAsync(Guid userId)
        {
            return await _gateway.GetUnreadNotificationsAsync(userId);
        }

        public async Task MarkAsReadAsync(Guid notificationId)
        {
            await _gateway.MarkAsReadAsync(notificationId);
        }
    }
}
