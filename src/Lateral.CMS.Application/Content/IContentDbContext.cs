using NuvTools.Data.EntityFrameworkCore.Context;

namespace Lateral.CMS.Application.Content;

public interface IContentDbContext : IDbContextCommands
{
    DbSet<Domain.Entities.Content.CmsEntity> CmsEntity { get; set; }
    DbSet<Domain.Entities.Content.CmsEntityTombstone> CmsEntityTombstone { get; set; }
}
