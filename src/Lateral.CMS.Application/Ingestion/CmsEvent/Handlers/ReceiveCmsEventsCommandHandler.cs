using FluentValidation;
using Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Lateral.CMS.Application.Ingestion.CmsEvent.Validators;
using Lateral.CMS.Application.Security;
using Lateral.CMS.Domain.Enumerations;
using CmsEventEntity = Lateral.CMS.Domain.Entities.Ingestion.CmsEvent;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Handlers;

/// <summary>
/// Webhook intake. Validates and sanitizes each event, stores the whole batch in the inbox in one transaction and
/// returns. Processing happens asynchronously (<see cref="CmsEventProcessor"/>), so the CMS gets a fast
/// acknowledgement and a received batch is never lost once it was acknowledged.
/// </summary>
public class ReceiveCmsEventsCommandHandler(
    IIngestionDbContext context,
    IValidator<ReceiveCmsEventsCommand> batchValidator,
    IValidator<CmsEventRequest> eventValidator,
    CmsPayloadSanitizer payloadSanitizer,
    ICmsEventProcessingSignal processingSignal,
    ICurrentUserService currentUserService,
    IDateTimeService dateTimeService,
    ILogger<ReceiveCmsEventsCommandHandler> logger)
        : IRequestHandler<ReceiveCmsEventsCommand, IResult<CmsEventBatchReceiptDTO>>
{
    private const int StatusReasonMaxLength = 2000;

    public async Task<IResult<CmsEventBatchReceiptDTO>> Handle(
        ReceiveCmsEventsCommand request,
        CancellationToken cancellationToken)
    {
        var batchValidation = await batchValidator.ValidateAsync(request, cancellationToken);

        if (!batchValidation.IsValid)
        {
            var errors = batchValidation.Errors.Select(e => e.ErrorMessage).ToList();
            logger.LogWarning("CMS event batch rejected: {Errors}", string.Join(" | ", errors));
            return Result<CmsEventBatchReceiptDTO>.ValidationFail(errors);
        }

        var receipt = new CmsEventBatchReceiptDTO
        {
            BatchId = Guid.CreateVersion7(),
            Received = request.Events.Count
        };

        var receivedDate = dateTimeService.UtcNowOffset;
        var receivedBy = currentUserService.CurrentUser.UserName ?? "unknown";

        for (var index = 0; index < request.Events.Count; index++)
        {
            var (cmsEvent, errors) = await BuildEventAsync(request.Events[index], index, receipt.BatchId, receivedDate, receivedBy, cancellationToken);

            context.CmsEvent.Add(cmsEvent);

            if (errors.Count > 0)
            {
                receipt.Rejections.Add(new CmsEventRejectionDTO { Index = index, Id = cmsEvent.ExternalId, Errors = errors });

                logger.LogWarning(
                    "CMS event rejected. Batch: {BatchId}, Index: {BatchIndex}, Type: {EventType}, Id: {ExternalId}, Errors: {Errors}",
                    receipt.BatchId, index, cmsEvent.CmsEventTypeId, cmsEvent.ExternalId, cmsEvent.StatusReason);
            }
        }

        receipt.Accepted = receipt.Received - receipt.Rejected;

        await context.SaveChangesAsync(cancellationToken);

        if (receipt.Accepted > 0)
            processingSignal.Notify();

        logger.LogInformation(
            "CMS event batch {BatchId} received from {ReceivedBy}: {Received} events, {Accepted} accepted, {Rejected} rejected.",
            receipt.BatchId, receivedBy, receipt.Received, receipt.Accepted, receipt.Rejected);

        return Result<CmsEventBatchReceiptDTO>.Success(receipt);
    }

    private async Task<(CmsEventEntity Event, List<string> Errors)> BuildEventAsync(
        CmsEventRequest? item,
        int index,
        Guid batchId,
        DateTimeOffset receivedDate,
        string receivedBy,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        string? payload = null;
        CmsEventType? type = null;

        if (item is null)
        {
            errors.Add("The event must be a JSON object.");
        }
        else
        {
            var validation = await eventValidator.ValidateAsync(item, cancellationToken);
            errors.AddRange(validation.Errors.Select(e => e.ErrorMessage));

            if (CmsEventTypeParser.TryParse(item.Type, out var parsedType))
                type = parsedType;

            if (errors.Count == 0 && CmsEventTypeParser.IsVersioned(parsedType))
            {
                var sanitized = payloadSanitizer.Sanitize(item.Payload!.Value);

                if (sanitized.Succeeded)
                    payload = sanitized.Payload;
                else
                    errors.Add(sanitized.Error!);
            }
        }

        var rejected = errors.Count > 0;

        var cmsEvent = new CmsEventEntity
        {
            BatchId = batchId,
            BatchIndex = index,
            CmsEventTypeId = type,
            ExternalId = TextSanitizer.ForStorage(item?.Id, CmsEventRequestValidator.ExternalIdMaxLength),
            Version = type == CmsEventType.Delete ? null : item?.Version,
            Payload = payload,
            EventTimestamp = item?.Timestamp is { } timestamp ? CmsEventTimestamp.Normalize(timestamp) : null,
            CmsEventStatusId = rejected ? CmsEventStatus.Rejected : CmsEventStatus.Pending,
            StatusReason = rejected ? TextSanitizer.ForStorage(string.Join(" | ", errors), StatusReasonMaxLength) : null,
            ProcessedDate = rejected ? receivedDate : null,
            ReceivedDate = receivedDate,
            ReceivedBy = receivedBy
        };

        return (cmsEvent, errors);
    }
}
