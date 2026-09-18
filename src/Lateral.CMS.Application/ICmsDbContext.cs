using Lateral.CMS.Application.Content;
using Lateral.CMS.Application.Ingestion;

namespace Lateral.CMS.Application;

/// <summary>
/// Writer context (primary database). Command handlers and the event processor use it; API reads go
/// through <see cref="ICmsReadOnlyDbContext"/>.
/// </summary>
public interface ICmsDbContext : IContentDbContext, IIngestionDbContext
{
}
