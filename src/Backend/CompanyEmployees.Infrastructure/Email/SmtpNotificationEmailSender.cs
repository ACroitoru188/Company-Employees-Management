using System.Net;
using System.Net.Mail;
using System.Text;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyEmployees.Infrastructure.Email
{
    /// <summary>
    /// Emails a copy of a notification over SMTP.
    ///
    /// The SmtpClient plumbing here is deliberately its own rather than shared with
    /// <c>Web/Services/AccountEmailSender.cs</c>, which sends password-setup invitations. That
    /// one is live and sits in a layer this project cannot reference; unifying them means moving
    /// it down here, which is a refactor of working code for no behaviour. Both read the same
    /// <c>Smtp</c> configuration section, so there is one place to configure — but two places to
    /// fix if the transport itself ever needs changing. Worth revisiting if a third sender appears.
    /// </summary>
    public sealed class SmtpNotificationEmailSender : INotificationEmailSender
    {
        private readonly NotificationEmailOptions _options;
        private readonly ILogger<SmtpNotificationEmailSender> _logger;

        public SmtpNotificationEmailSender(
            IOptions<NotificationEmailOptions> options,
            ILogger<SmtpNotificationEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<bool> SendAsync(
            User recipient,
            Notification notification,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(recipient.Email))
            {
                _logger.LogWarning("No address on file for {UserId}; notification not emailed.", recipient.Id);
                return false;
            }

            // Unconfigured is the normal state in development, and it is not an error: the
            // notification itself has already been saved and shown in the app.
            if (string.IsNullOrWhiteSpace(_options.Host) || string.IsNullOrWhiteSpace(_options.FromAddress))
            {
                _logger.LogInformation(
                    "SMTP is not configured; notification for {Email} not emailed: {Message}",
                    recipient.Email, notification.Message);
                return false;
            }

            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(_options.FromAddress, _options.FromName),
                    Subject = "Siemens Time Off — " + Summarise(notification.Message),
                    Body = BuildBody(recipient, notification),
                    IsBodyHtml = true,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                };
                message.To.Add(recipient.Email);

                using var client = new SmtpClient(_options.Host, _options.Port)
                {
                    EnableSsl = _options.EnableSsl,
                    // The caller is a request that has already done its real work, so the mail
                    // server gets a few seconds and no more.
                    Timeout = _options.TimeoutSeconds * 1000
                };

                if (!string.IsNullOrWhiteSpace(_options.Username))
                {
                    client.Credentials = new NetworkCredential(_options.Username, _options.Password);
                }

                await client.SendMailAsync(message, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not email notification {NotificationId} to {Email}.",
                    notification.Id, recipient.Email);
                return false;
            }
        }

        // The subject line is the notification's own first sentence, so the mailbox shows what
        // happened without opening anything.
        private static string Summarise(string message)
        {
            var firstStop = message.IndexOf('.');
            var summary = firstStop > 0 ? message[..firstStop] : message;
            return summary.Length <= 80 ? summary : summary[..77] + "...";
        }

        private string BuildBody(User recipient, Notification notification)
        {
            var greeting = WebUtility.HtmlEncode(recipient.Name?.Split(' ').FirstOrDefault() ?? "there");
            var body = WebUtility.HtmlEncode(notification.Message);
            var link = AbsoluteLink(notification.ActionUrl);

            // ActionUrl is stored relative ("/employee/my-requests"), which is meaningless in a
            // mailbox. Without a configured BaseUrl the message still carries its full text —
            // it just cannot offer a button.
            var action = link is null
                ? ""
                : $"""
                   <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin-top:24px;">
                     <tr><td bgcolor="#1E88E5" style="border-radius:6px;">
                       <a href="{link}" style="display:inline-block;padding:12px 22px;font-size:15px;font-weight:600;line-height:1;color:#ffffff;text-decoration:none;border-radius:6px;">Open in Time Off</a>
                     </td></tr>
                   </table>
                   """;

            return $"""
                    <!doctype html>
                    <html><body style="margin:0;padding:24px;background:#f5f5f5;font-family:'Segoe UI',Arial,sans-serif;color:#1a1a1a;">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">
                        <tr><td align="center">
                          <table role="presentation" width="560" cellspacing="0" cellpadding="0" border="0" style="max-width:560px;background:#ffffff;border-radius:10px;padding:32px;">
                            <tr><td>
                              <p style="margin:0 0 18px;font-size:15px;line-height:1.6;">Hello {greeting},</p>
                              <p style="margin:0;font-size:16px;line-height:1.6;">{body}</p>
                              {action}
                              <p style="margin:28px 0 0;font-size:12px;line-height:1.5;color:#616161;">
                                Siemens Time Off Management &middot; automated message, no reply needed.
                              </p>
                            </td></tr>
                          </table>
                        </td></tr>
                      </table>
                    </body></html>
                    """;
        }

        private string? AbsoluteLink(string? actionUrl)
        {
            if (string.IsNullOrWhiteSpace(actionUrl) || string.IsNullOrWhiteSpace(_options.BaseUrl))
                return null;

            return WebUtility.HtmlEncode($"{_options.BaseUrl.TrimEnd('/')}/{actionUrl.TrimStart('/')}");
        }
    }

    /// <summary>
    /// Bound to the <c>Smtp</c> section, the same one the password-invitation sender reads.
    /// </summary>
    public sealed class NotificationEmailOptions
    {
        public const string SectionName = "Smtp";

        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromAddress { get; set; } = "";
        public string FromName { get; set; } = "Siemens Time Off";

        /// <summary>How long the mail server gets before the send is abandoned.</summary>
        public int TimeoutSeconds { get; set; } = 5;

        /// <summary>
        /// Where the app is reachable, e.g. <c>https://timeoff.siemens.com</c>. Needed only to
        /// turn a notification's relative ActionUrl into a link a mailbox can follow.
        /// </summary>
        public string BaseUrl { get; set; } = "";
    }
}
