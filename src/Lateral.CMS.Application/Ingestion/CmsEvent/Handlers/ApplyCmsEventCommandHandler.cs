using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Lateral.CMS.Domain.Enumerations;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Handlers;

public class ApplyCmsEventCommandHandler(
    ICmsDbContext context,
    CmsEventApplier applier,
    IDateTimeService dateTimeService,
    ILogger<ApplyCmsEventCommandHandler> logger)
        : IRequestHandler<ApplyCmsEventCommand, IResult<CmsEventStatus>>
{
    public async Task<IResult<CmsEventStatus>> Handle(
        ApplyCmsEventCommand request,
        CancellationToken cancellationToken)
    {
        var cmsEvent = await context.CmsEvent.FirstOrDefaultAsync(e => e.CmsEventId == request.CmsEventId, cancellationToken);

        if (cmsEvent is null)
            return Result<CmsEventStatus>.FailNotFound($"CMS event {request.CmsEventId} was not found.");

        // Already handled (e.g. by another instance): processing is at-least-once, applying is idempotent.
        if (cmsEvent.CmsEventStatusId != CmsEventStatus.Pending)
            return Result<CmsEventStatus>.Success(cmsEvent.CmsEventStatusId);

        var outcome = await applier.ApplyAsync(cmsEvent, cancellationToken);

        cmsEvent.CmsEventStatusId = outcome.Status;
        cmsEvent.StatusReason = outcome.Reason;
        cmsEvent.Attempts++;
        cmsEvent.NextAttemptDate = null;
        cmsEvent.ProcessedDate = dateTimeService.UtcNowOffset;
        cmsEvent.Payload = null;

        // Entity change + inbox status in a single SaveChanges: one transaction.
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "CMS event {CmsEventId} processed. Outcome: {Outcome}, Type: {EventType}, Id: {ExternalId}, Version: {Version}, Timestamp: {EventTimestamp}, Batch: {BatchId}, Reason: {Reason}",
            cmsEvent.CmsEventId, outcome.Status, cmsEvent.CmsEventTypeId, cmsEvent.ExternalId, cmsEvent.Version,
            cmsEvent.EventTimestamp, cmsEvent.BatchId, outcome.Reason);

        return Result<CmsEventStatus>.Success(outcome.Status);
    }
}
