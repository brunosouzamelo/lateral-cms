using Lateral.CMS.Domain.Entities.Content;
using Lateral.CMS.Domain.Enumerations;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Services;

public enum CmsEventOrder
{
    Newer,
    Duplicate,
    Stale
}

/// <summary>
/// Decides whether a publish/unPublish event is newer than the state already stored for the entity.
/// </summary>
public static class CmsEventOrdering
{
    /// <summary>
    /// <list type="number">
    /// <item>The version is authoritative: a higher version is newer, a lower one is stale.</item>
    /// <item>For the same version, the event timestamp decides (e.g. publish v4 → unPublish v4 → publish v4).</item>
    /// <item>Same version and timestamp with the same resulting status is a re-delivery (duplicate).</item>
    /// <item>Same version and timestamp but a different status is ambiguous: the restrictive status
    /// (unpublished) wins, so confidential data is never re-exposed by delivery order.</item>
    /// </list>
    /// </summary>
    public static CmsEventOrder Compare(int version, DateTimeOffset timestamp, CmsEntityStatus targetStatus, CmsEntity entity)
    {
        if (version != entity.Version)
            return version > entity.Version ? CmsEventOrder.Newer : CmsEventOrder.Stale;

        if (timestamp != entity.LastEventTimestamp)
            return timestamp > entity.LastEventTimestamp ? CmsEventOrder.Newer : CmsEventOrder.Stale;

        if (targetStatus == entity.CmsEntityStatusId)
            return CmsEventOrder.Duplicate;

        return targetStatus == CmsEntityStatus.Unpublished ? CmsEventOrder.Newer : CmsEventOrder.Stale;
    }
}
