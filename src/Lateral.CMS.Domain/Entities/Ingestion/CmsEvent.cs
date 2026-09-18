using Lateral.CMS.Domain.Enumerations;

namespace Lateral.CMS.Domain.Entities.Ingestion;

/// <summary>
/// Inbox row for one event received through the webhook. Accepted events wait as
/// <see cref="CmsEventStatus.Pending"/> until the background processor applies them.
/// </summary>
public class CmsEvent
{
    public long CmsEventId { get; set; }
    public required Guid BatchId { get; set; }

    /// <summary>Position of the event inside the received batch.</summary>
    public required int BatchIndex { get; set; }

    /// <summary>Null when the event was rejected because its type is missing or unsupported.</summary>
    public CmsEventType? CmsEventTypeId { get; set; }

    public string? ExternalId { get; set; }
    public int? Version { get; set; }

    /// <summary>Sanitized payload. Cleared once the event is applied or ignored (data minimization).</summary>
    public string? Payload { get; set; }

    public DateTimeOffset? EventTimestamp { get; set; }

    public required CmsEventStatus CmsEventStatusId { get; set; }
    public string? StatusReason { get; set; }

    /// <summary>Processing attempts. Also the optimistic concurrency token of the row.</summary>
    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptDate { get; set; }
    public DateTimeOffset? ProcessedDate { get; set; }

    public required DateTimeOffset ReceivedDate { get; set; }
    public required string ReceivedBy { get; set; }
}
