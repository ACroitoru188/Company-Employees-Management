using Microsoft.AspNetCore.SignalR;

namespace CompanyEmployees.Application.Hubs
{
    public class NotificationHub : Hub
    {
        public Task Register(string userId)
        {
            return Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }
    }
}
