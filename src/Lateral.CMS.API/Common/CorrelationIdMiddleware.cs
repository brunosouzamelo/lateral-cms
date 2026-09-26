using Lateral.CMS.Application.Common;

namespace Lateral.CMS.API.Common;

/// <summary>
/// Carries a correlation identifier through the request. The CMS can send its own with
/// <c>X-Correlation-ID</c> so one delivery can be followed across both systems; otherwise the trace
/// identifier of the request is used. Every log line written while the request runs carries it, because
/// the identifier is pushed as a logging scope around the rest of the pipeline.
/// </summary>
/// <remarks>
/// It is also put on <see cref="HttpContext.Items"/>, where <see cref="CorrelationContext"/> picks it up
/// for the application layer to store alongside the events of the delivery.
/// </remarks>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>Key under which the resolved identifier is placed on the request.</summary>
    public const string ItemKey = "Lateral.CMS.CorrelationId";

    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The value is echoed back, stored and logged, so it is sanitized like any other untrusted input.
        var correlationId = TextSanitizer.ForStorage(context.Request.Headers[HeaderName].FirstOrDefault(), MaxLength);

        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = context.TraceIdentifier;

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        // The message-template overload, rather than a dictionary: it names the property for a structured
        // sink and still reads as text, the way the framework's own scopes do.
        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            await next(context);
        }
    }
}
