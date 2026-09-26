using Lateral.CMS.Application.Common;

namespace Lateral.CMS.API.Common;

/// <summary>
/// Hands the application layer the correlation identifier <see cref="CorrelationIdMiddleware"/> resolved
/// for the request, without it having to know about HTTP.
/// </summary>
public class CorrelationContext(IHttpContextAccessor httpContextAccessor) : ICorrelationContext
{
    /// <summary>
    /// Null outside a request — the background processor has no caller to correlate with, and the events
    /// it handles already carry the identifier of the delivery that brought them.
    /// </summary>
    public string? CorrelationId
        => httpContextAccessor.HttpContext?.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) == true
            ? value as string
            : null;
}
