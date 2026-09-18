using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using NuvTools.Data.EntityFrameworkCore.Design;

namespace Lateral.CMS.Infrastructure.Data.SqlServer;

/// <summary>Design-time factory used by <c>dotnet ef</c> (see MigrationCommands.txt).</summary>
public class CmsDbContextFactory : DesignTimeDbContextFactoryBase<CmsDbContext>
{
    private const string DATABASE_PROPERTY = "Database";
    private const string APPSETTINGS_NAME = "appsettings.json";

    protected override CmsDbContext CreateNewInstance(DbContextOptionsBuilder<CmsDbContext> optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        InitializeConfiguration(new JsonConfigurationSource { Path = APPSETTINGS_NAME });

        optionsBuilder.UseSqlServer(Configuration!.GetConnectionString(DATABASE_PROPERTY), b => b.MigrationsAssembly(GetType().Assembly.FullName));

        return new CmsDbContext(optionsBuilder.Options);
    }
}
