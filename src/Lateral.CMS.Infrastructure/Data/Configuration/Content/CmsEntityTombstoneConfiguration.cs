using Lateral.CMS.Application.Ingestion.CmsEvent.Validators;
using Lateral.CMS.Domain.Entities.Content;

namespace Lateral.CMS.Infrastructure.Data.Configuration.Content;

public class CmsEntityTombstoneConfiguration : IEntityTypeConfiguration<CmsEntityTombstone>
{
    public void Configure(EntityTypeBuilder<CmsEntityTombstone> builder)
    {
        builder.ToTable(nameof(CmsEntityTombstone), nameof(Content));
        builder.HasKey(e => e.ExternalId);

        builder.Property(e => e.ExternalId).HasMaxLength(CmsEventRequestValidator.ExternalIdMaxLength);
        builder.Property(e => e.DeletedTimestamp).IsConcurrencyToken();
    }
}
