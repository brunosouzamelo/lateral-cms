using Microsoft.EntityFrameworkCore;

namespace Lateral.CMS.API.Common;

public static class DatabaseStartupExtensions
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Applies pending migrations, waiting for the database to accept connections.
    /// </summary>
    /// <remarks>
    /// A database container started next to the API takes tens of seconds to answer, and an orchestrator
    /// restarts the database from under a running service. Failing at the first refused connection would make
    /// start-up depend on that timing, so connection failures are retried until <paramref name="timeout"/>
    /// elapses — after which the host still gives up, because a service that cannot reach its database has
    /// nothing useful to do.
    /// </remarks>
    public static async Task MigrateDatabaseAsync<TContext>(this IHost host, TimeSpan timeout, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(host);

        var logger = host.Services.GetRequiredService<ILogger<TContext>>();
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();

            try
            {
                await context.Database.MigrateAsync(cancellationToken);

                logger.LogInformation("The database is up to date.");
                return;
            }
            catch (Exception exception) when (DateTimeOffset.UtcNow + RetryDelay < deadline)
            {
                logger.LogWarning("The database is not reachable yet ({Reason}). Retrying in {RetryDelay}.",
                    exception.GetBaseException().Message, RetryDelay);

                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
