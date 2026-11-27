using System.Threading.Channels;

namespace appdevPUPSCOUT.Services;

using System.Threading.Channels;

public interface IEmailQueue
{
    ValueTask<bool> EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default);
    ChannelReader<EmailMessage> Reader { get; }
    void Complete();
}
