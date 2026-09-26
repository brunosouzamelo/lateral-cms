using Lateral.CMS.Application.Common;

namespace Lateral.CMS.API.Common;

/// <summary>
/// Carries a correlation identifier through the request. The CMS can send its own with
/// <c>X-Correlation-ID</c> so one delivery can be followed across both systems; otherwise the trace
/// identifier of the request is used. Every log line written while the request runs carries it, because
/// the identifier is pushed as a logging scope around the rest of the pipeline.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The value is echoed back and logged, so it is sanitized like any other untrusted input.
        var correlationId = TextSanitizer.ForStorage(context.Request.Headers[HeaderName].FirstOrDefault(), MaxLength);

        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = context.TraceIdentifier;

        context.Response.Headers[HeaderName] = correlationId;

        // The message-template overload, rather than a dictionary: it names the property for a structured
        // sink and still reads as text, the way the framework's own scopes do.
        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            await next(context);
        }
    }
}
