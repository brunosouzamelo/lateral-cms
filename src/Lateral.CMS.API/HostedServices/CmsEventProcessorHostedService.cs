using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.API.HostedServices;

/// <summary>
/// Drains the event inbox in the background. The webhook only stores the batch and acknowledges it, so the CMS
/// is never blocked by processing and a slow or failing event cannot make it retry a batch already received.
/// </summary>
/// <remarks>
/// A pass runs as soon as a batch arrives (<see cref="ICmsEventProcessingSignal"/>) and, at the latest, after
/// <see cref="IngestionOptions.PollingInterval"/> — the poll also picks up events left behind by a restart and
/// retries that became due. When a pass fills its batch, the next one starts immediately.
/// </remarks>
public class CmsEventProcessorHostedService(
    CmsEventProcessor processor,
    ICmsEventProcessingSignal processingSignal,
    IOptions<IngestionOptions> options,
    ILogger<CmsEventProcessorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.ProcessInBackground)
        {
            logger.LogInformation("In-process CMS event processing is disabled ('Ingestion:ProcessInBackground').");
            return;
        }

        logger.LogInformation("CMS event processor started. Batch size: {BatchSize}, polling interval: {PollingInterval}.",
            settings.ProcessingBatchSize, settings.PollingInterval);

        // Events received before this instance started (or left pending by a crash) are picked up by the first pass.
        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;

            try
            {
                processed = await processor.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Reaching here means the pass itself failed (e.g. the database is down), not a single event:
                // individual failures are recorded and retried by the processor.
                logger.LogError(exception, "The CMS event processing pass failed. Retrying in {PollingInterval}.", settings.PollingInterval);

                await SafeDelayAsync(settings.PollingInterval, stoppingToken);
                continue;
            }

            // A full batch means more events are probably waiting: keep going instead of sleeping.
            if (processed >= settings.ProcessingBatchSize)
                continue;

            await processingSignal.WaitAsync(settings.PollingInterval, stoppingToken);
        }

        logger.LogInformation("CMS event processor stopped.");
    }

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
