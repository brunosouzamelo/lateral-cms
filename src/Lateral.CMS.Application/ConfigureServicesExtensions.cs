using FluentValidation;
using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Lateral.CMS.Application;

public static class ConfigureServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDateTimeService, DateTimeService>();

        services.AddSingleton<CmsPayloadSanitizer>();
        services.AddScoped<CmsEventApplier>();
        services.AddSingleton<CmsEventProcessor>();

        return services;
    }
}
