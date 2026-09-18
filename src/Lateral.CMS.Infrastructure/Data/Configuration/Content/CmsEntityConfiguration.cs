using Lateral.CMS.Application.Ingestion.CmsEvent.Validators;
using Lateral.CMS.Domain.Entities.Content;
using Lateral.CMS.Infrastructure.Data.Configuration.Common;

namespace Lateral.CMS.Infrastructure.Data.Configuration.Content;

public class CmsEntityConfiguration : AuditModifiedBaseConfiguration<CmsEntity>
{
    public override void Configure(EntityTypeBuilder<CmsEntity> builder)
    {
        base.Configure(builder);
        builder.ToTable(nameof(CmsEntity), nameof(Content));
        builder.HasKey(e => e.CmsEntityId);

        builder.Property(e => e.ExternalId).HasMaxLength(CmsEventRequestValidator.ExternalIdMaxLength);
        builder.Property(e => e.Payload).IsRequired();
        builder.Property(e => e.DisabledByAdminUser).HasMaxLength(50);
        builder.Property(e => e.ConcurrencyToken).IsConcurrencyToken();

        builder.HasIndex(e => e.ExternalId).IsUnique();

        // Covers the user listing (published and enabled, ordered by id) without touching disabled rows.
        builder.HasIndex(e => new { e.CmsEntityStatusId, e.IsDisabledByAdmin, e.ExternalId });
    }
}
