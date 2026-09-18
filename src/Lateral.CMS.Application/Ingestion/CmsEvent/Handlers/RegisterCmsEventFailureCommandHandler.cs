using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Lateral.CMS.Domain.Enumerations;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Handlers;

/// <summary>
/// Records a processing failure: schedules a retry with exponential back-off or, after the maximum number of
/// attempts, marks the event as <see cref="CmsEventStatus.Failed"/> (the payload is kept for a manual replay).
/// </summary>
public class RegisterCmsEventFailureCommandHandler(
    IIngestionDbContext context,
    IOptions<IngestionOptions> options,
    IDateTimeService dateTimeService,
    ILogger<RegisterCmsEventFailureCommandHandler> logger)
        : IRequestHandler<RegisterCmsEventFailureCommand, IResult>
{
    private const int StatusReasonMaxLength = 2000;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(10);

    public async Task<IResult> Handle(
        RegisterCmsEventFailureCommand request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cmsEvent = await context.CmsEvent.FirstOrDefaultAsync(e => e.CmsEventId == request.CmsEventId, cancellationToken);

        if (cmsEvent is null || cmsEvent.CmsEventStatusId != CmsEventStatus.Pending)
            return Result.Success();

        var now = dateTimeService.UtcNowOffset;

        cmsEvent.Attempts++;
        cmsEvent.StatusReason = TextSanitizer.ForStorage(request.Error, StatusReasonMaxLength);

        if (cmsEvent.Attempts >= settings.MaxProcessingAttempts)
        {
            cmsEvent.CmsEventStatusId = CmsEventStatus.Failed;
            cmsEvent.NextAttemptDate = null;
            cmsEvent.ProcessedDate = now;

            logger.LogError(
                "CMS event {CmsEventId} failed permanently after {Attempts} attempts. Type: {EventType}, Id: {ExternalId}, Version: {Version}, Batch: {BatchId}, Error: {Error}",
                cmsEvent.CmsEventId, cmsEvent.Attempts, cmsEvent.CmsEventTypeId, cmsEvent.ExternalId, cmsEvent.Version, cmsEvent.BatchId, cmsEvent.StatusReason);
        }
        else
        {
            var delay = TimeSpan.FromTicks(Math.Min(
                settings.RetryBaseDelay.Ticks * (1L << Math.Min(cmsEvent.Attempts - 1, 20)),
                MaxRetryDelay.Ticks));

            cmsEvent.NextAttemptDate = now + delay;

            logger.LogWarning(
                "CMS event {CmsEventId} failed (attempt {Attempts} of {MaxAttempts}); retrying at {NextAttemptDate}. Type: {EventType}, Id: {ExternalId}, Version: {Version}, Error: {Error}",
                cmsEvent.CmsEventId, cmsEvent.Attempts, settings.MaxProcessingAttempts, cmsEvent.NextAttemptDate,
                cmsEvent.CmsEventTypeId, cmsEvent.ExternalId, cmsEvent.Version, cmsEvent.StatusReason);
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
