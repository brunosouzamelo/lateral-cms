namespace Lateral.CMS.Application.Ingestion.CmsEvent.Services;

/// <summary>Wakes the event processor as soon as a batch is stored, instead of waiting for the next poll.</summary>
public interface ICmsEventProcessingSignal
{
    void Notify();

    /// <summary>Completes when <see cref="Notify"/> is called or <paramref name="timeout"/> elapses.</summary>
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
