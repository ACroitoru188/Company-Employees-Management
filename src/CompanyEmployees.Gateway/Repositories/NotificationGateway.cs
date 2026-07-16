using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public async Task<List<Notification>> GetUnreadNotificationsAsync(Guid userId)
        {
            try
            {
                return await _context.Notifications
                    .Where(n => n.UserId == userId && !n.IsRead)
                    .ToListAsync();
            }
            catch
            {
                // Tabelul lipsește sau baza de date nu răspunde. 
                // Returnăm o listă goală ca să nu "crape" tot ecranul.
                return new List<Notification>();
            }
        }

        public async Task MarkAsReadAsync(Guid notificationId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }
        }
    }
}
