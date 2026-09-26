using Lateral.CMS.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lateral.CMS.API.Common;

/// <summary>
/// Readiness probe: the API is only useful once the writer database answers, since the webhook must be able
/// to store an incoming batch before acknowledging it.
/// </summary>
public class DatabaseHealthCheck(ICmsDbContext cmsDbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (cmsDbContext is not DbContext dbContext)
            return HealthCheckResult.Unhealthy("The database context is not an EF Core context.");

        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database did not answer.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("The database did not answer.", exception);
        }
    }
}
