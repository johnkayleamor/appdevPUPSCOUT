using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace appdevPUPSCOUT.Services;

public sealed class EmailSenderBackgroundService(IServiceScopeFactory scopeFactory, IEmailQueue queue, ILogger<EmailSenderBackgroundService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IEmailQueue _queue = queue;
    private readonly ILogger<EmailSenderBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await SendWithRetryAsync(message, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            while (_queue.Reader.TryRead(out var message))
            {
                await SendWithRetryAsync(message, CancellationToken.None);
            }
        }
    }

    private async Task SendWithRetryAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>();
                await sender.SendEmailAsync(message.To, message.Subject, message.Body);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email send failed (type {Type}, to {MaskedEmail}), attempt {Attempt}/3.", message.Type, MaskEmail(message.To), attempt);
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken == CancellationToken.None ? CancellationToken.None : cancellationToken);
                }
            }
        }
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***" + email.Substring(Math.Max(0, at));
        return email[0] + "***" + email.Substring(at);
    }
}
