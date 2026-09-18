using NuvTools.Data.EntityFrameworkCore.Context;

namespace Lateral.CMS.Infrastructure.Data;

/// <summary>
/// Provider-agnostic model. Provider specifics (column types, naming convention, migrations) live in the
/// derived context of the provider project, e.g. <c>Lateral.CMS.Infrastructure.Data.SqlServer</c>.
/// </summary>
public partial class CmsDbContext(DbContextOptions options) : DbContextBase(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(assembly: typeof(CmsDbContext).Assembly);
    }
}
