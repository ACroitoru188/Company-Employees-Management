using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Application.Notifications
{
    // Created is null when only read state moved; subscribers answer that by re-reading
    // instead of mirroring the change.
    public sealed record NotificationChange(Notification? Created)
    {
        public static NotificationChange ForCreated(Notification notification) => new(notification);

        public static NotificationChange ReadStateChanged { get; } = new(Created: null);
    }
}
