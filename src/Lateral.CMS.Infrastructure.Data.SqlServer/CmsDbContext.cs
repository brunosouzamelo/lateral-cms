using Lateral.CMS.Application;
using Lateral.CMS.Domain.Entities.Content;
using Lateral.CMS.Domain.Entities.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace Lateral.CMS.Infrastructure.Data.SqlServer;

/// <summary>Writer context, bound to the primary database (connection string <c>Database</c>).</summary>
public class CmsDbContext : Data.CmsDbContext, ICmsDbContext
{
    public CmsDbContext(DbContextOptions<CmsDbContext> options) : base(options)
    {
    }

    protected CmsDbContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // SQL Server has no JSON column type: payloads are nvarchar(max) with a check constraint, which is
        // the part of jsonb worth keeping here — the database refuses to store anything that is not JSON.
        builder.Entity<CmsEntity>()
            .ToTable(t => t.HasCheckConstraint("CK_CmsEntity_Payload_IsJson", "ISJSON([Payload]) > 0"));

        builder.Entity<CmsEvent>()
            .ToTable(t => t.HasCheckConstraint("CK_CmsEvent_Payload_IsJson", "ISJSON([Payload]) > 0"));
    }
}
