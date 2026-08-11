using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using NSubstitute;

namespace CompanyEmployees.Application.Tests;

public class NotificationContextTests
{
    private readonly INotificationGateway _gateway = Substitute.For<INotificationGateway>();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    [Fact]
    public async Task SendNotificationAsync_persists_and_publishes_the_notification()
    {
        var userId = Guid.NewGuid();
        var context = new NotificationContext(_gateway, _dispatcher);

        var result = await context.SendNotificationAsync(userId, "Request approved", "/requests");

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("Request approved", result.Message);
        Assert.Equal("/requests", result.ActionUrl);
        Assert.False(result.IsRead);
        await _gateway.Received(1).CreateNotificationAsync(result);
        await _dispatcher.Received(1).PublishCreatedAsync(userId, result);
    }

    [Fact]
    public async Task MarkAsReadAsync_updates_storage_and_notifies_subscribers()
    {
        var userId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var context = new NotificationContext(_gateway, _dispatcher);

        await context.MarkAsReadAsync(userId, notificationId);

        Received.InOrder(() =>
        {
            _gateway.MarkAsReadAsync(userId, notificationId);
            _dispatcher.PublishReadStateChangedAsync(userId);
        });
    }
}
