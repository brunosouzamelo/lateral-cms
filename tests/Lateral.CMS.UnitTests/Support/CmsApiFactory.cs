using Lateral.CMS.Application;
using Lateral.CMS.Application.Content;
using Lateral.CMS.Application.Ingestion;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Lateral.CMS.Domain.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SqlServerCmsDbContext = Lateral.CMS.Infrastructure.Data.SqlServer.CmsDbContext;
using SqlServerCmsReadOnlyDbContext = Lateral.CMS.Infrastructure.Data.SqlServer.CmsReadOnlyDbContext;

namespace Lateral.CMS.UnitTests.Support;

/// <summary>
/// The real API — its routing, authentication, authorization, model binding and handlers — hosted in memory
/// over SQLite, so the tests exercise the service end to end without any infrastructure.
/// </summary>
/// <remarks>
/// Two deliberate differences from a deployed instance: one database stands in for the writer/reader pair,
/// and the background processor is off so a test decides when events are applied
/// (see <see cref="ProcessEventsAsync"/>) instead of racing with it.
/// </remarks>
public class CmsApiFactory : WebApplicationFactory<Program>
{
    public const string OrganizationUser = "cms-webhook-test";
    public const string OrganizationPassword = "b1c2d3e4-f5a6-4b7c-8d9e-0f1a2b3c4d5e";

    public const string ConsumerUser = "content-consumer-t";
    public const string ConsumerPassword = "c2d3e4f5-a6b7-4c8d-9e0f-1a2b3c4d5e6f";

    public const string AdminUser = "content-admin-test";
    public const string AdminPassword = "d3e4f5a6-b7c8-4d9e-8f1a-2b3c4d5e6f70";

    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.HttpLogging", "Warning");

        // Applied by the test itself, so an assertion never has to wait for a background pass.
        builder.UseSetting("Ingestion:ProcessInBackground", "false");

        ConfigureUser(builder, 0, OrganizationUser, OrganizationPassword, Roles.Organization);
        ConfigureUser(builder, 1, ConsumerUser, ConsumerPassword, Roles.User);
        ConfigureUser(builder, 2, AdminUser, AdminPassword, Roles.User, Roles.Admin);

        builder.ConfigureTestServices(services =>
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.RemoveAll<DbContextOptions<SqlServerCmsDbContext>>();
            services.RemoveAll<DbContextOptions<SqlServerCmsReadOnlyDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<SqlServerCmsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SqlServerCmsReadOnlyDbContext>>();
            services.RemoveAll<SqlServerCmsDbContext>();
            services.RemoveAll<SqlServerCmsReadOnlyDbContext>();

            services.AddDbContext<TestCmsDbContext>(options => options.UseSqlite(_connection));

            services.AddScoped<ICmsDbContext>(sp => sp.GetRequiredService<TestCmsDbContext>());
            services.AddScoped<IContentDbContext>(sp => sp.GetRequiredService<TestCmsDbContext>());
            services.AddScoped<IIngestionDbContext>(sp => sp.GetRequiredService<TestCmsDbContext>());
            services.AddScoped<ICmsReadOnlyDbContext>(sp => sp.GetRequiredService<TestCmsDbContext>());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCmsDbContext>()
            .GetService<IRelationalDatabaseCreator>().CreateTables();

        return host;
    }

    /// <summary>Runs one processing pass, as the background service would.</summary>
    public Task<int> ProcessEventsAsync()
        => Services.GetRequiredService<CmsEventProcessor>().ProcessPendingAsync(CancellationToken.None);

    private static void ConfigureUser(IWebHostBuilder builder, int index, string userName, string password, params string[] roles)
    {
        builder.UseSetting($"Authentication:Basic:Users:{index}:UserName", userName);
        builder.UseSetting($"Authentication:Basic:Users:{index}:Password", password);

        for (var role = 0; role < roles.Length; role++)
            builder.UseSetting($"Authentication:Basic:Users:{index}:Roles:{role}", roles[role]);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _connection?.Dispose();
    }
}
