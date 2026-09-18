namespace Lateral.CMS.Domain.Entities.Common;

public abstract class AuditModifiedBase
{
    public required DateTimeOffset AddedDate { get; set; }
    public DateTimeOffset? ModifiedDate { get; set; }
}
