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
            Body = BuildPasswordSetupEmail(employeeName, accountEmail, setupLink),
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
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

    private static string BuildPasswordSetupEmail(
        string employeeName,
        string accountEmail,
        string setupLink)
    {
        var safeName = WebUtility.HtmlEncode(employeeName);
        var safeEmail = WebUtility.HtmlEncode(accountEmail);
        var safeLink = WebUtility.HtmlEncode(setupLink);

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Set up your password</title>
            </head>
            <body style="margin:0;background:#eef3f5;font-family:Arial,Helvetica,sans-serif;color:#1b2638;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background:#eef3f5;">
                    <tr>
                        <td align="center" style="padding:40px 16px;">
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="max-width:600px;background:#ffffff;border:1px solid #dce4e8;border-radius:16px;overflow:hidden;box-shadow:0 12px 30px rgba(22,44,70,.08);">
                                <tr><td style="height:6px;background:#009999;font-size:0;line-height:0;">&nbsp;</td></tr>
                                <tr><td style="padding:34px 42px 8px;"><div style="font-size:22px;font-weight:700;letter-spacing:.08em;color:#009999;">SIEMENS</div></td></tr>
                                <tr>
                                    <td style="padding:22px 42px 38px;">
                                        <div style="font-size:12px;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:#607080;margin-bottom:12px;">Time Off Management</div>
                                        <h1 style="font-size:28px;line-height:1.25;margin:0 0 16px;color:#17243a;">Create your password</h1>
                                        <p style="font-size:16px;line-height:1.65;margin:0 0 12px;color:#46566a;">Hello {safeName},</p>
                                        <p style="font-size:16px;line-height:1.65;margin:0 0 22px;color:#46566a;">Your employee account is ready. Use the button below to choose a secure password and finish setting up your access.</p>
                                        <div style="background:#f4f8f9;border:1px solid #dce7ea;border-radius:10px;padding:14px 16px;margin-bottom:26px;">
                                            <div style="font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:.08em;color:#71808f;margin-bottom:5px;">Your sign-in email</div>
                                            <div style="font-size:16px;font-weight:600;color:#17243a;word-break:break-all;">{safeEmail}</div>
                                        </div>
                                        <table role="presentation" cellspacing="0" cellpadding="0" border="0"><tr><td bgcolor="#006b72" style="border-radius:9px;">
                                            <a href="{safeLink}" style="display:inline-block;padding:14px 26px;font-size:16px;font-weight:700;line-height:1;color:#ffffff;text-decoration:none;border-radius:9px;">Create my password</a>
                                        </td></tr></table>
                                        <p style="font-size:13px;line-height:1.6;margin:24px 0 0;color:#71808f;">This secure, one-time link expires in 24 hours. If you were not expecting this account, you can safely ignore this email.</p>
                                        <div style="border-top:1px solid #e5ebee;margin-top:26px;padding-top:20px;">
                                            <p style="font-size:12px;line-height:1.55;margin:0 0 7px;color:#7d8995;">Button not working? Copy and paste this link into your browser:</p>
                                            <a href="{safeLink}" style="font-size:12px;line-height:1.55;color:#006b72;word-break:break-all;">{safeLink}</a>
                                        </div>
                                    </td>
                                </tr>
                            </table>
                            <p style="font-size:12px;line-height:1.5;margin:18px 0 0;color:#7b8792;">Siemens Time Off Management &middot; Automated account message</p>
                        </td>
                    </tr>
                </table>
            </body>
            </html>
            """;
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
