using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Services;

/// <summary>
/// Runs one processing pass over the inbox. Each event is applied in its own DI scope (fresh DbContext and
/// transaction), so a failing event is isolated from the rest of the pass and retried later.
/// </summary>
public class CmsEventProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> options,
    ILogger<CmsEventProcessor> logger)
{
    /// <returns>Number of events picked in the pass.</returns>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        IList<long> pendingIds;

        using (var scope = scopeFactory.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(new GetPendingCmsEventIdsQuery { Take = options.Value.ProcessingBatchSize }, cancellationToken);
            pendingIds = result.Data ?? [];
        }

        foreach (var cmsEventId in pendingIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessAsync(cmsEventId, cancellationToken);
        }

        return pendingIds.Count;
    }

    private async Task ProcessAsync(long cmsEventId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            await mediator.Send(new ApplyCmsEventCommand { CmsEventId = cmsEventId }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while applying CMS event {CmsEventId}.", cmsEventId);
            await RegisterFailureAsync(cmsEventId, ex, cancellationToken);
        }
    }

    private async Task RegisterFailureAsync(long cmsEventId, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            await mediator.Send(new RegisterCmsEventFailureCommand
            {
                CmsEventId = cmsEventId,
                Error = $"{exception.GetType().Name}: {exception.GetBaseException().Message}"
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The event stays Pending and is picked again on the next pass.
            logger.LogError(ex, "Could not register the failure of CMS event {CmsEventId}.", cmsEventId);
        }
    }
}
