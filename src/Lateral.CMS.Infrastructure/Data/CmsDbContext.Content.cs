using Lateral.CMS.Application.Content;
using Lateral.CMS.Domain.Entities.Content;

namespace Lateral.CMS.Infrastructure.Data;

public partial class CmsDbContext : IContentDbContext
{
    public DbSet<CmsEntity> CmsEntity { get; set; }
    public DbSet<CmsEntityTombstone> CmsEntityTombstone { get; set; }
}
