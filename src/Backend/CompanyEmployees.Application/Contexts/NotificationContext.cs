using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;

namespace CompanyEmployees.Application.Contexts
{
    public class NotificationContext
    {
        private readonly INotificationGateway _gateway;
        private readonly INotificationDispatcher _dispatcher;
        private readonly IUserGateway _users;
        private readonly INotificationEmailSender _email;

        public NotificationContext(
            INotificationGateway gateway,
            INotificationDispatcher dispatcher,
            IUserGateway users,
            INotificationEmailSender email)
        {
            _gateway = gateway;
            _dispatcher = dispatcher;
            _users = users;
            _email = email;
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
            await EmailAsync(userId, notification);
            return notification;
        }

        /// <summary>
        /// Mails a copy of the notification to its recipient.
        ///
        /// Every notification in the app is created here, so this one place covers all five
        /// callers — a manager's decision, HR's, a contract extended or terminated, and a
        /// delegation assigned. Nothing above it needs to know an email went out.
        ///
        /// Best-effort, like the notification sends it accompanies: the row is already saved and
        /// on the recipient's screen by this point, so neither a missing address nor a mail
        /// server having a bad day may take the caller's action down with it. The sender swallows
        /// its own failures; this catch is for the user lookup and for anything it does not.
        /// </summary>
        private async Task EmailAsync(Guid userId, Notification notification)
        {
            try
            {
                var recipient = await _users.GetUserByIdAsync(userId);
                if (recipient is not null)
                    await _email.SendAsync(recipient, notification);
            }
            catch
            {
                // Deliberately silent: the sender logs its own failures, and a notification must
                // not be lost because of a mailbox.
            }
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
