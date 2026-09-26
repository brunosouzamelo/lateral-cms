using Microsoft.AspNetCore.Mvc;
using NuvTools.Common.ResultWrapper;
using NuvTools.Common.ResultWrapper.Enumerations;
using IResult = NuvTools.Common.ResultWrapper.IResult;

namespace Lateral.CMS.API.Common;

/// <summary>
/// Translates the <see cref="IResult"/> returned by every handler into an HTTP response, so the mapping
/// between an application outcome and a status code is written once.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<TData> Respond<TData>(IResult<TData> result)
        => result.Succeeded ? Ok(result.Data) : Problem(result);

    protected ActionResult Respond(IResult result)
        => result.Succeeded ? NoContent() : Problem(result);

    /// <summary>
    /// 202 for work that was taken in but not carried out yet — the webhook stores the batch and the
    /// processor applies it afterwards.
    /// </summary>
    protected ActionResult<TData> RespondAccepted<TData>(IResult<TData> result)
        => result.Succeeded ? Accepted(result.Data) : Problem(result);

    private ObjectResult Problem(IResult result)
    {
        var errors = result.Messages
            .Select(m => new ApiError(m.Code, m.Title, m.Detail))
            .ToList();

        var (status, title) = result switch
        {
            { ResultType: ResultType.ValidationError } => (StatusCodes.Status400BadRequest, "The request is not valid."),
            { ContainsNotFound: true } => (StatusCodes.Status404NotFound, "The resource was not found."),
            _ => (StatusCodes.Status409Conflict, "The request conflicts with the current state.")
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = errors.Count == 1 ? errors[0].Message : null,
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["errors"] = errors;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return StatusCode(status, problem);
    }
}

public sealed record ApiError(string? Code, string Message, string? Detail);
