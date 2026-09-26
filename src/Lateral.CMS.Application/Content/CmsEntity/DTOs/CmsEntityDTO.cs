using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Lateral.CMS.Application.Common.Serialization;
using Lateral.CMS.Domain.Enumerations;
using CmsEntityEntity = Lateral.CMS.Domain.Entities.Content.CmsEntity;

namespace Lateral.CMS.Application.Content.CmsEntity.DTOs;

/// <summary>
/// An entity as this service holds it: the latest data the CMS has sent, plus the local admin override
/// on top of it. The two are kept apart — <see cref="Status"/> is the CMS's decision,
/// <see cref="IsDisabledByAdmin"/> is this service's, and neither affects the other.
/// </summary>
public class CmsEntityDTO
{
    /// <summary>Identifier the CMS assigned to the entity. Unique, and stable across its versions.</summary>
    public required string Id { get; set; }

    /// <summary>
    /// Latest version this service has been given, published or not. This is the version
    /// <see cref="Payload"/> belongs to.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Last version that arrived through a publish. Lower than <see cref="Version"/> when a newer version
    /// reached this service only by being unpublished, and null when no version was ever published.
    /// </summary>
    public int? LastPublishedVersion { get; set; }

    /// <summary>Whether the CMS currently has the entity published. See <see cref="CmsEntityStatus"/>.</summary>
    public CmsEntityStatus Status { get; set; }

    /// <summary>
    /// Whether an administrator hid the entity here. A local override: it never reaches the CMS and
    /// survives later CMS events. Consumers never see an entity for which this is true.
    /// </summary>
    public bool IsDisabledByAdmin { get; set; }

    /// <summary>When the override was applied (UTC). Null while the entity is not disabled.</summary>
    public DateTimeOffset? DisabledByAdminDate { get; set; }

    /// <summary>
    /// Timestamp of the CMS event that produced the current state (UTC) — that is, when the entity last
    /// changed in the CMS, not when this service stored it.
    /// </summary>
    public DateTimeOffset LastEventTimestamp { get; set; }

    /// <summary>When this service first stored the entity (UTC).</summary>
    public DateTimeOffset AddedDate { get; set; }

    /// <summary>When this service last changed it (UTC). Null when nothing changed since it was stored.</summary>
    public DateTimeOffset? ModifiedDate { get; set; }

    /// <summary>
    /// The entity fields at <see cref="Version"/>, exactly as the CMS sent them once sanitized. Returned
    /// as JSON rather than as an escaped string, so a consumer does not have to parse it a second time.
    /// </summary>
    [JsonConverter(typeof(RawJsonStringConverter))]
    public required string Payload { get; set; }

    /// <summary>Server-side projection: only the returned columns are read, nothing is tracked.</summary>
    public static readonly Expression<Func<CmsEntityEntity, CmsEntityDTO>> Projection = e => new CmsEntityDTO
    {
        Id = e.ExternalId,
        Version = e.Version,
        LastPublishedVersion = e.LastPublishedVersion,
        Status = e.CmsEntityStatusId,
        IsDisabledByAdmin = e.IsDisabledByAdmin,
        DisabledByAdminDate = e.DisabledByAdminDate,
        LastEventTimestamp = e.LastEventTimestamp,
        AddedDate = e.AddedDate,
        ModifiedDate = e.ModifiedDate,
        Payload = e.Payload
    };
}
