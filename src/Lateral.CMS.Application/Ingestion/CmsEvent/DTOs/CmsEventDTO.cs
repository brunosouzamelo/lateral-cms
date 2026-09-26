using System.Linq.Expressions;
using Lateral.CMS.Domain.Enumerations;
using CmsEventEntity = Lateral.CMS.Domain.Entities.Ingestion.CmsEvent;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;

/// <summary>
/// Processing record of one received event: what it was, what became of it and why. Administrators only,
/// and the payload is never part of it — the answer to "what did not get through" needs no entity data.
/// </summary>
public class CmsEventDTO
{
    /// <summary>Identifier of the inbox row. Also the order in which events were received.</summary>
    public long CmsEventId { get; set; }

    /// <summary>The delivery this event arrived in.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Zero-based position of the event inside that delivery.</summary>
    public int BatchIndex { get; set; }

    /// <summary>
    /// What the CMS reported: <c>Publish</c> made a version public, <c>UnPublish</c> withdrew it while
    /// keeping the data, <c>Delete</c> removed the entity. Null when the event was refused because its
    /// type was missing or unsupported.
    /// </summary>
    public CmsEventType? Type { get; set; }

    /// <summary>Identifier of the entity the event was about, as the CMS sent it.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Version the event carried. Null for a delete, and for a refused event that had none.</summary>
    public int? Version { get; set; }

    /// <summary>When the event happened in the CMS (UTC), normalized from what was sent.</summary>
    public DateTimeOffset? EventTimestamp { get; set; }

    /// <summary>Where the event ended up. See <see cref="CmsEventStatus"/>.</summary>
    public CmsEventStatus Status { get; set; }

    /// <summary>
    /// Why it ended up there, in words: which rule it broke, why it was skipped as a duplicate or stale
    /// delivery, or the error that keeps failing it.
    /// </summary>
    public string? StatusReason { get; set; }

    /// <summary>
    /// How many times processing has been attempted. Above one means earlier attempts failed and were
    /// retried; it reaches the configured maximum only for a <see cref="CmsEventStatus.Failed"/> event.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>When the next retry becomes due (UTC). Null when none is scheduled.</summary>
    public DateTimeOffset? NextAttemptDate { get; set; }

    /// <summary>When the event reached a final status (UTC). Null while it is still pending.</summary>
    public DateTimeOffset? ProcessedDate { get; set; }

    /// <summary>When the webhook received the event (UTC).</summary>
    public DateTimeOffset ReceivedDate { get; set; }

    /// <summary>The authenticated account that delivered it.</summary>
    public string? ReceivedBy { get; set; }

    /// <summary>
    /// Identifier of the request that brought the event. It is the <c>X-Correlation-ID</c> the CMS sent,
    /// when it sent one, so a delivery can be followed across both systems and in the logs of either.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>Server-side projection: only the returned columns are read, nothing is tracked.</summary>
    public static readonly Expression<Func<CmsEventEntity, CmsEventDTO>> Projection = e => new CmsEventDTO
    {
        CmsEventId = e.CmsEventId,
        BatchId = e.BatchId,
        BatchIndex = e.BatchIndex,
        Type = e.CmsEventTypeId,
        ExternalId = e.ExternalId,
        Version = e.Version,
        EventTimestamp = e.EventTimestamp,
        Status = e.CmsEventStatusId,
        StatusReason = e.StatusReason,
        Attempts = e.Attempts,
        NextAttemptDate = e.NextAttemptDate,
        ProcessedDate = e.ProcessedDate,
        ReceivedDate = e.ReceivedDate,
        ReceivedBy = e.ReceivedBy,
        CorrelationId = e.CorrelationId
    };
}
