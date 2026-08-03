using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace CompanyEmployees.Web.Services;

public interface IAccountEmailSender
{
    Task<EmailDeliveryResult> SendPasswordSetupAsync(
        string employeeName,
        string invitationEmail,
        string accountEmail,
        string setupLink);
}

public sealed class SmtpAccountEmailSender : IAccountEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpAccountEmailSender> _logger;

    public SmtpAccountEmailSender(
        IOptions<SmtpOptions> options,
        ILogger<SmtpAccountEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailDeliveryResult> SendPasswordSetupAsync(
        string employeeName,
        string invitationEmail,
        string accountEmail,
        string setupLink)
    {
        if (string.IsNullOrWhiteSpace(_options.Host)
            || string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            _logger.LogWarning(
                "SMTP is not configured. Password setup link for {Email}: {SetupLink}",
                invitationEmail,
                setupLink);
            return new EmailDeliveryResult(false);
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = "Set up your Siemens Time Off password",
            Body = $"""
                Hello {employeeName},

                An account has been created for you in Siemens Time Off.

                Your sign-in email is: {accountEmail}

                Set your password using this one-time link (valid for 24 hours):
                {setupLink}

                If you were not expecting this account, you can ignore this email.
                """
        };
        message.To.Add(invitationEmail);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        await client.SendMailAsync(message);
        return new EmailDeliveryResult(true);
    }
}

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Siemens Time Off";
}

public sealed record EmailDeliveryResult(bool Delivered);
