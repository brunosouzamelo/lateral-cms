using System.Threading.Channels;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;

namespace Lateral.CMS.Infrastructure.Messaging;

/// <summary>
/// In-memory wake-up signal for the processor hosted in the same process. Notifications coalesce (capacity 1):
/// one pending signal is enough because a pass drains every pending event. With several instances the database
/// polling still guarantees progress; a broker (e.g. Service Bus) would replace this class.
/// </summary>
public class InProcessCmsEventProcessingSignal : ICmsEventProcessingSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    public void Notify() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await _channel.Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout elapsed: time for a regular poll.
        }
    }
}
