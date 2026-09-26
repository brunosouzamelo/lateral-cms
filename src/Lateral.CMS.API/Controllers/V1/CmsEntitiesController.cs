using Asp.Versioning;
using Lateral.CMS.API.Common;
using Lateral.CMS.API.Models;
using Lateral.CMS.Application.Content.CmsEntity.DTOs;
using Lateral.CMS.Application.Content.CmsEntity.Requests;
using Lateral.CMS.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuvTools.Data.Paging;

namespace Lateral.CMS.API.Controllers.V1;

/// <summary>
/// Entities mirrored from the CMS. Everything here is treated as confidential, so no route is anonymous.
/// </summary>
/// <remarks>
/// One set of endpoints serves both audiences; what changes is what the query returns. A consumer sees the
/// entities that are published in the CMS and not disabled locally, an administrator sees all of them —
/// see <see cref="Application.Content.CmsEntity.Services.CmsEntityVisibility"/>.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/entities")]
[Produces("application/json")]
[Authorize(Policy = Policies.ContentReader)]
public class CmsEntitiesController(ISender sender) : ApiControllerBase
{
    /// <summary>Lists the entities visible to the caller.</summary>
    /// <remarks>The <c>status</c> and <c>isDisabledByAdmin</c> filters only apply to administrators.</remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagingWithEnumerableList<CmsEntityDTO>>> GetPaged(
        [FromQuery] GetPagedCmsEntityQuery query,
        CancellationToken cancellationToken)
            => Respond(await sender.Send(query, cancellationToken));

    /// <summary>Returns one entity by the identifier the CMS assigned to it.</summary>
    /// <response code="404">The entity does not exist, or is not visible to the caller.</response>
    [HttpGet("{id}", Name = nameof(GetById))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CmsEntityDTO>> GetById(string id, CancellationToken cancellationToken)
        => Respond(await sender.Send(new GetByCmsEntityIdQuery { Id = id }, cancellationToken));

    /// <summary>
    /// Disables or re-enables an entity locally. This is an overwrite kept by this service: it hides the
    /// entity from consumers and is never sent back to the CMS, whose own data is left untouched.
    /// </summary>
    /// <remarks>
    /// The only write a caller can make. CMS data — payload, version and published status — cannot be changed
    /// through the API by any user; it only changes when the CMS sends an event.
    /// </remarks>
    [HttpPut("{id}/disabled")]
    [Authorize(Policy = Policies.ContentAdministrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetDisabled(
        string id,
        [FromBody] SetCmsEntityDisabledRequest request,
        CancellationToken cancellationToken)
            => Respond(await sender.Send(
                new SetCmsEntityDisabledCommand { Id = id, IsDisabled = request?.IsDisabled ?? false },
                cancellationToken));
}
