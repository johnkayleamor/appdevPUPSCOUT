using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace appdevPUPSCOUT.Services;

public class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender, IAppEmailService
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<SmtpEmailSender> _logger = logger;

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        try
        {
            var host = _configuration["Email:Host"] ?? string.Empty;
            var port = _configuration.GetValue<int>("Email:Port", 587);
            var useStartTls = _configuration.GetValue<bool>("Email:UseStartTls", true);
            var senderName = _configuration["Email:SenderName"] ?? "PUPSCOUT";
            var senderAddress = _configuration["Email:SenderAddress"] ?? string.Empty;
            var username = _configuration["Email:Username"] ?? string.Empty;
            var password = _configuration["Email:Password"]
                ?? Environment.GetEnvironmentVariable("EMAIL_PASSWORD")
                ?? string.Empty;

            using var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderAddress));
            message.To.Add(MailboxAddress.Parse(email));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlMessage };

            using var client = new SmtpClient();
            var socketOptions = useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(host, port, socketOptions);

            if (!string.IsNullOrWhiteSpace(username))
            {
                await client.AuthenticateAsync(username, password);
            }

            await client.SendAsync(new SendRequest(message));
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMTP email to {Email}.", email);
        }
    }
}
