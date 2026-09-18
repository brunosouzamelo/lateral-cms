using Lateral.CMS.Application;
using Lateral.CMS.Domain.Entities.Content;
using Lateral.CMS.Domain.Entities.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace Lateral.CMS.Infrastructure.Data.SqlServer;

/// <summary>
/// Reader context, bound to the connection string <c>DatabaseReadOnly</c> (a replica; falls back to the primary).
/// Shares the model of <see cref="CmsDbContext"/>, is registered with no-tracking queries and refuses to save.
/// Migrations are applied only through the writer context.
/// </summary>
public class CmsReadOnlyDbContext(DbContextOptions<CmsReadOnlyDbContext> options) : CmsDbContext(options), ICmsReadOnlyDbContext
{
    IQueryable<CmsEntity> ICmsReadOnlyDbContext.CmsEntity => CmsEntity.AsNoTracking();
    IQueryable<CmsEvent> ICmsReadOnlyDbContext.CmsEvent => CmsEvent.AsNoTracking();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw ReadOnlyDbContextGuard.SaveNotAllowed(GetType());

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        => throw ReadOnlyDbContextGuard.SaveNotAllowed(GetType());
}
