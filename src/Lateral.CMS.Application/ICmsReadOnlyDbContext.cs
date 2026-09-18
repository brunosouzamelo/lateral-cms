using Lateral.CMS.Domain.Entities.Content;
using Lateral.CMS.Domain.Entities.Ingestion;

namespace Lateral.CMS.Application;

/// <summary>
/// Reader context. Bound to the read-only connection string (a replica when available), never tracks
/// entities and refuses <c>SaveChanges</c>. Exposes <see cref="IQueryable{T}"/> only, so writes do not compile.
/// </summary>
public interface ICmsReadOnlyDbContext
{
    IQueryable<CmsEntity> CmsEntity { get; }
    IQueryable<CmsEvent> CmsEvent { get; }
}
