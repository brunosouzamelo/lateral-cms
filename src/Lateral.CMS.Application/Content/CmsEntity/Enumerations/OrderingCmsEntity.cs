namespace Lateral.CMS.Application.Content.CmsEntity.Enumerations;

/// <summary>What the entity listing can be sorted by, through <c>sortColumn</c>.</summary>
public enum OrderingCmsEntity
{
    /// <summary>The identifier the CMS assigned. The default, and stable across pages.</summary>
    Id,

    /// <summary>
    /// When the entity last changed in the CMS. Ties are broken by identifier, so paging stays stable
    /// even when several entities share a timestamp.
    /// </summary>
    LastEventTimestamp
}
