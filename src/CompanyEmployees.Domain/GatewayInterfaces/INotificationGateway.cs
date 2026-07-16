using CompanyEmployees.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface INotificationGateway
    {
        Task<List<Notification>> GetUnreadNotificationsAsync(Guid userId);
        Task MarkAsReadAsync(Guid notificationId);
        Task<Notification> CreateNotificationAsync(Notification notification);
    }
}
