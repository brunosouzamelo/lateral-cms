namespace Lateral.CMS.Infrastructure.Data;

public static class ReadOnlyDbContextGuard
{
    public static InvalidOperationException SaveNotAllowed(Type contextType)
        => new($"{contextType.Name} is read-only. Use the writer context (ICmsDbContext) to persist changes.");
}
