using Microsoft.AspNetCore.HttpLogging;

namespace Lateral.CMS.API.Common;

/// <summary>
/// Adds to the per-request log line the fields this API is actually asked about: who called, from where,
/// and under which correlation identifier.
/// </summary>
public class CmsHttpLoggingInterceptor : IHttpLoggingInterceptor
{
    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)
    {
        ArgumentNullException.ThrowIfNull(logContext);

        var request = logContext.HttpContext.Request;

        logContext.AddParameter("ClientIp", logContext.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        logContext.AddParameter("UserAgent", request.Headers.UserAgent.ToString());

        return default;
    }

    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext)
    {
        ArgumentNullException.ThrowIfNull(logContext);

        var context = logContext.HttpContext;

        // Only known once authentication has run, which is why it is added on the way out.
        logContext.AddParameter("UserName", context.User.Identity?.Name ?? "anonymous");
        logContext.AddParameter("CorrelationId", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());

        return default;
    }
}
