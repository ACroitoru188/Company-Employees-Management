using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Gateway.Repositories
{
    public class NotificationGateway : BaseRepository, INotificationGateway
    {
        public NotificationGateway(CompanyEmployeesDbContext context) : base(context)
        {
        }

        public async Task<Notification> CreateNotificationAsync(Notification notification)
        {
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
            return notification;
        }

        public async Task<List<Notification>> GetRecentAsync(Guid userId, int take)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Notification>> GetPageAsync(Guid userId, int skip, int take)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Skip(skip)
                .Take(take)
                .AsNoTracking()
                .ToListAsync();
        }

        public Task<int> CountAsync(Guid userId) =>
            _context.Notifications.CountAsync(n => n.UserId == userId);

        public Task<int> GetUnreadCountAsync(Guid userId) =>
            _context.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

        // Both writes go through ExecuteUpdate: one statement, and no entity to load first.
        // It bypasses the change tracker, so anything already tracked here stays stale.
        public Task MarkAsReadAsync(Guid userId, Guid notificationId) =>
            _context.Notifications
                .Where(n => n.Id == notificationId && n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true));

        public Task MarkAllAsReadAsync(Guid userId) =>
            _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true));
    }
}
