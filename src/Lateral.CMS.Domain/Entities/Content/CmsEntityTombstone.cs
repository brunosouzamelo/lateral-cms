namespace Lateral.CMS.Domain.Entities.Content;

/// <summary>
/// Deletion marker for a hard-deleted entity. Keeps no entity data — only the identifier and the most
/// recent deletion timestamp — so late or re-delivered events that predate the deletion cannot resurrect it.
/// </summary>
public class CmsEntityTombstone
{
    public required string ExternalId { get; set; }
    public required DateTimeOffset DeletedTimestamp { get; set; }
    public required DateTimeOffset AddedDate { get; set; }
}
