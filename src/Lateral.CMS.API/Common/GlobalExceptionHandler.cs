using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lateral.CMS.API.Common;

/// <summary>
/// Last line of defence: turns an unhandled exception into the same problem details shape the rest of the
/// API returns, and keeps the exception itself out of the response.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var (status, title, code) = exception switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                "The resource changed while the request was being handled.", "concurrency.conflict"),

            DbUpdateException => (StatusCodes.Status409Conflict,
                "The request conflicts with the current state.", "database.constraint_violation"),

            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (ClientClosedRequest,
                "The client closed the request.", "request.cancelled"),

            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "unexpected")
        };

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception while handling {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogWarning(exception, "Request {Method} {Path} failed: {Title}", httpContext.Request.Method, httpContext.Request.Path, title);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = httpContext.Request.Path
        };

        problem.Extensions["errors"] = new[] { new ApiError(code, title, null) };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
