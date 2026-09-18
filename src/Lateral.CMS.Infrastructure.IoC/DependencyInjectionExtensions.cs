using Lateral.CMS.Application;
using Lateral.CMS.Application.Content;
using Lateral.CMS.Application.Ingestion;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Lateral.CMS.Infrastructure.Data.SqlServer;
using Lateral.CMS.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NuvTools.Data.EntityFrameworkCore.SqlServer.Extensions;

namespace Lateral.CMS.Infrastructure.IoC;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var writerConnectionString = configuration.GetConnectionString("Database");
        var readerConnectionString = configuration.GetConnectionString("DatabaseReadOnly");

        if (string.IsNullOrWhiteSpace(readerConnectionString))
            readerConnectionString = writerConnectionString;

        // Writer: commands and event processing.
        services.AddDatabase<CmsDbContext>(
            writerConnectionString!,
            b => b.MigrationsAssembly(typeof(CmsDbContextFactory).Assembly.FullName));

        // Reader: API queries. No tracking (no snapshot/identity-map cost) and a separate connection pool,
        // so reads can be pointed to a replica without touching the code.
        services.AddDbContext<CmsReadOnlyDbContext>(options => options
            .UseSqlServer(readerConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddScoped<ICmsDbContext>(sp => sp.GetRequiredService<CmsDbContext>());
        services.AddScoped<IContentDbContext>(sp => sp.GetRequiredService<CmsDbContext>());
        services.AddScoped<IIngestionDbContext>(sp => sp.GetRequiredService<CmsDbContext>());
        services.AddScoped<ICmsReadOnlyDbContext>(sp => sp.GetRequiredService<CmsReadOnlyDbContext>());

        services.AddSingleton<ICmsEventProcessingSignal, InProcessCmsEventProcessingSignal>();

        services.AddApplicationServices(configuration);

        return services;
    }
}
