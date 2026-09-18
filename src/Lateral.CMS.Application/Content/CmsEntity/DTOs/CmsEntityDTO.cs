using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Lateral.CMS.Application.Common.Serialization;
using Lateral.CMS.Domain.Enumerations;
using CmsEntityEntity = Lateral.CMS.Domain.Entities.Content.CmsEntity;

namespace Lateral.CMS.Application.Content.CmsEntity.DTOs;

public class CmsEntityDTO
{
    public required string Id { get; set; }
    public int Version { get; set; }
    public int? LastPublishedVersion { get; set; }
    public CmsEntityStatus Status { get; set; }
    public bool IsDisabledByAdmin { get; set; }
    public DateTimeOffset? DisabledByAdminDate { get; set; }
    public DateTimeOffset LastEventTimestamp { get; set; }
    public DateTimeOffset AddedDate { get; set; }
    public DateTimeOffset? ModifiedDate { get; set; }

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
