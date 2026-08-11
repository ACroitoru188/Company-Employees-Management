using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    /// <summary>
    /// Sends a copy of a notification to the recipient's mailbox.
    ///
    /// An outbound service, so it is declared here alongside <see cref="IPublicHolidayProvider"/>
    /// and implemented in Infrastructure: the Application layer that raises notifications must
    /// not have to know what SMTP is, and the dependency has to point inward.
    /// </summary>
    public interface INotificationEmailSender
    {
        /// <summary>
        /// Best-effort by contract: an implementation reports failure rather than throwing, and
        /// a notification is never lost because its email could not be delivered.
        /// </summary>
        /// <returns><c>true</c> when the message was handed to the mail server.</returns>
        Task<bool> SendAsync(User recipient, Notification notification, CancellationToken cancellationToken = default);
    }
}
