using Lateral.CMS.Domain.Entities.Common;

namespace Lateral.CMS.Infrastructure.Data.Configuration.Common;

public abstract class AuditModifiedBaseConfiguration<T> : IEntityTypeConfiguration<T> where T : AuditModifiedBase
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.Property(e => e.AddedDate).IsRequired();
    }
}
