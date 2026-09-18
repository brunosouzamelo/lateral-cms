using NuvTools.Data.EntityFrameworkCore.Context;

namespace Lateral.CMS.Application.Ingestion;

public interface IIngestionDbContext : IDbContextCommands
{
    DbSet<Domain.Entities.Ingestion.CmsEvent> CmsEvent { get; set; }
}
