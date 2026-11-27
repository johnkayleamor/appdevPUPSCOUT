using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace appdevPUPSCOUT.Services;

public sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    private readonly ILogger<EmailQueue> _logger;

    public EmailQueue(ILogger<EmailQueue> logger)
    {
        _logger = logger;
    }

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Complete() => _channel.Writer.TryComplete();

    public ValueTask<bool> EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        // Try to queue without waiting. The channel is bounded.
        if (_channel.Writer.TryWrite(message))
        {
            return ValueTask.FromResult(true);
        }

        var isOtp = message.Type is "EmailConfirmationOtp" or "PasswordResetOtp";

        if (isOtp)
        {
            _logger.LogWarning("Email queue is full; could not queue {Type} email to {MaskedEmail}.", message.Type, MaskEmail(message.To));
            return ValueTask.FromResult(false);
        }

        // For non-OTP emails, drop the oldest queued message to make room.
        if (_channel.Reader.TryRead(out var dropped))
        {
            _logger.LogWarning("Email queue is full; dropped {Type} email to {MaskedEmail} to queue {NewType}.", dropped.Type, MaskEmail(dropped.To), message.Type);
        }

        if (_channel.Writer.TryWrite(message))
        {
            return ValueTask.FromResult(true);
        }

        _logger.LogWarning("Email queue is full; could not queue {Type} email to {MaskedEmail}.", message.Type, MaskEmail(message.To));
        return ValueTask.FromResult(false);
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***" + email.Substring(Math.Max(0, at));
        return email[0] + "***" + email.Substring(at);
    }
}