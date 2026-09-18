using System.Linq.Expressions;
using Lateral.CMS.Domain.Enumerations;
using CmsEventEntity = Lateral.CMS.Domain.Entities.Ingestion.CmsEvent;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;

/// <summary>Processing record of an event, for monitoring. Never exposes the payload.</summary>
public class CmsEventDTO
{
    public long CmsEventId { get; set; }
    public Guid BatchId { get; set; }
    public int BatchIndex { get; set; }
    public CmsEventType? Type { get; set; }
    public string? ExternalId { get; set; }
    public int? Version { get; set; }
    public DateTimeOffset? EventTimestamp { get; set; }
    public CmsEventStatus Status { get; set; }
    public string? StatusReason { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptDate { get; set; }
    public DateTimeOffset? ProcessedDate { get; set; }
    public DateTimeOffset ReceivedDate { get; set; }
    public string? ReceivedBy { get; set; }

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
        ReceivedBy = e.ReceivedBy
    };
}
