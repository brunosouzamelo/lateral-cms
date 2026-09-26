using Lateral.CMS.Domain.Enumerations;
using CmsEventEntity = Lateral.CMS.Domain.Entities.Ingestion.CmsEvent;

namespace Lateral.CMS.UnitTests.Support;

/// <summary>Accepted inbox events, as the webhook would have stored them.</summary>
public static class TestCmsEvents
{
    public static readonly DateTimeOffset BaseTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset At(int minutes) => BaseTimestamp.AddMinutes(minutes);

    public static CmsEventEntity Publish(string externalId, int version, DateTimeOffset timestamp, string? payload = null)
        => Versioned(CmsEventType.Publish, externalId, version, timestamp, payload);

    public static CmsEventEntity UnPublish(string externalId, int version, DateTimeOffset timestamp, string? payload = null)
        => Versioned(CmsEventType.UnPublish, externalId, version, timestamp, payload);

    public static CmsEventEntity Delete(string externalId, DateTimeOffset timestamp)
        => Create(CmsEventType.Delete, externalId, version: null, timestamp, payload: null);

    private static CmsEventEntity Versioned(CmsEventType type, string externalId, int version, DateTimeOffset timestamp, string? payload)
        => Create(type, externalId, version, timestamp, payload ?? $$"""{"title":"{{externalId}} v{{version}}"}""");

    private static CmsEventEntity Create(CmsEventType type, string externalId, int? version, DateTimeOffset timestamp, string? payload)
        => new()
        {
            BatchId = Guid.CreateVersion7(),
            BatchIndex = 0,
            CmsEventTypeId = type,
            ExternalId = externalId,
            Version = version,
            Payload = payload,
            EventTimestamp = timestamp,
            CmsEventStatusId = CmsEventStatus.Pending,
            ReceivedDate = timestamp,
            ReceivedBy = "cms-webhook-client"
        };
}
