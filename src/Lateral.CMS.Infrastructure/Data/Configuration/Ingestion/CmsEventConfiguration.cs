using Lateral.CMS.Application.Ingestion.CmsEvent.Validators;
using Lateral.CMS.Domain.Entities.Ingestion;

namespace Lateral.CMS.Infrastructure.Data.Configuration.Ingestion;

public class CmsEventConfiguration : IEntityTypeConfiguration<CmsEvent>
{
    public void Configure(EntityTypeBuilder<CmsEvent> builder)
    {
        builder.ToTable(nameof(CmsEvent), nameof(Ingestion));
        builder.HasKey(e => e.CmsEventId);
        builder.Property(e => e.CmsEventId).ValueGeneratedOnAdd();

        builder.Property(e => e.ExternalId).HasMaxLength(CmsEventRequestValidator.ExternalIdMaxLength);
        builder.Property(e => e.StatusReason).HasMaxLength(2000);
        builder.Property(e => e.ReceivedBy).HasMaxLength(50);

        // Two processors picking the same event: the second SaveChanges fails and the event is re-evaluated.
        builder.Property(e => e.Attempts).IsConcurrencyToken();

        // Pending pick-up query (status + retry window, ordered by CMS timestamp).
        builder.HasIndex(e => new { e.CmsEventStatusId, e.NextAttemptDate, e.EventTimestamp });
        builder.HasIndex(e => e.BatchId);
        builder.HasIndex(e => e.ExternalId);
    }
}
