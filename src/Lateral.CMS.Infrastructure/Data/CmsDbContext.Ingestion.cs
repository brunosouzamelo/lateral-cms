using Lateral.CMS.Application.Ingestion;
using Lateral.CMS.Domain.Entities.Ingestion;

namespace Lateral.CMS.Infrastructure.Data;

public partial class CmsDbContext : IIngestionDbContext
{
    public DbSet<CmsEvent> CmsEvent { get; set; }
}
