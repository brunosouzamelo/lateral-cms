using Lateral.CMS.Domain.Entities.Common;
using Lateral.CMS.Domain.Enumerations;

namespace Lateral.CMS.Domain.Entities.Content;

/// <summary>
/// Local copy of an entity managed by the CMS. Holds the most recent data version received from the
/// CMS (published or unpublished) plus the local admin override, which never flows back to the CMS.
/// </summary>
public class CmsEntity : AuditModifiedBase
{
    public required Guid CmsEntityId { get; set; }

    /// <summary>Identifier assigned by the CMS (the <c>id</c> of the events).</summary>
    public required string ExternalId { get; set; }

    /// <summary>Sanitized JSON of <see cref="Version"/>.</summary>
    public required string Payload { get; set; }

    /// <summary>Latest data version known for the entity, whether it was published or not.</summary>
    public required int Version { get; set; }

    /// <summary>Last version that reached the service through a publish event.</summary>
    public int? LastPublishedVersion { get; set; }

    public required CmsEntityStatus CmsEntityStatusId { get; set; }

    /// <summary>Timestamp (UTC) of the CMS event that produced the current state.</summary>
    public required DateTimeOffset LastEventTimestamp { get; set; }

    public bool IsDisabledByAdmin { get; set; }
    public DateTimeOffset? DisabledByAdminDate { get; set; }
    public string? DisabledByAdminUser { get; set; }

    /// <summary>Optimistic concurrency token, renewed on every write.</summary>
    public Guid ConcurrencyToken { get; set; }
}
