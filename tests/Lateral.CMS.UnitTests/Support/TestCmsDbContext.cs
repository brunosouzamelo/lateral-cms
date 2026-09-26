using Lateral.CMS.Application;
using Lateral.CMS.Domain.Entities.Content;
using Lateral.CMS.Domain.Entities.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lateral.CMS.UnitTests.Support;

/// <summary>
/// The shared, provider-agnostic model on SQLite, so tests exercise the real entity configurations
/// (keys, indexes, concurrency tokens) without a SQL Server instance.
/// </summary>
/// <remarks>
/// It plays both roles: the writer the handlers use and the reader the queries use. Splitting them here
/// would only test EF Core, not the service — but the read side still goes through
/// <see cref="ICmsReadOnlyDbContext"/>, so a handler that tried to write through it would not compile.
/// </remarks>
public class TestCmsDbContext(DbContextOptions<TestCmsDbContext> options)
    : Infrastructure.Data.CmsDbContext(options), ICmsDbContext, ICmsReadOnlyDbContext
{
    IQueryable<CmsEntity> ICmsReadOnlyDbContext.CmsEntity => CmsEntity.AsNoTracking();
    IQueryable<CmsEvent> ICmsReadOnlyDbContext.CmsEvent => CmsEvent.AsNoTracking();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // SQLite has no date type: stored as text, DateTimeOffset values would not compare or sort. Every
        // timestamp the service writes is UTC, so a lexicographic text comparison gives the right order.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToStringConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToStringConverter>();
    }
}
