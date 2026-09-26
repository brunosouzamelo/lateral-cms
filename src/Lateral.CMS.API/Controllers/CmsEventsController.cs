using Asp.Versioning;
using Lateral.CMS.API.Common;
using Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Lateral.CMS.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuvTools.Data.Paging;

namespace Lateral.CMS.API.Controllers;

/// <summary>
/// Webhook the CMS posts its event batches to.
/// </summary>
/// <remarks>
/// The route is fixed by the contract agreed with the CMS, so it is version-neutral and carries no
/// <c>/api/v1</c> prefix: the consumer API can version independently of the integration.
/// </remarks>
[ApiVersionNeutral]
[Route("cms/events")]
[Produces("application/json")]
public class CmsEventsController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Receives a batch of CMS events. Each event is validated and sanitized on its own: the invalid ones are
    /// reported in the receipt while the rest of the batch is accepted, so one malformed event does not make
    /// the CMS retry a whole delivery.
    /// </summary>
    /// <response code="202">The batch was stored. Processing happens asynchronously.</response>
    /// <response code="400">The batch itself is not usable (empty, too large or malformed JSON).</response>
    [HttpPost]
    [Authorize(Policy = Policies.CmsIngestion)]
    // The type is spelled out: for a status the action's return type does not imply, the attribute alone
    // would declare the response as having no body, and the receipt would be missing from the document.
    [ProducesResponseType<CmsEventBatchReceiptDTO>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CmsEventBatchReceiptDTO>> Receive(
        [FromBody] List<CmsEventRequest?> events,
        CancellationToken cancellationToken)
            => RespondAccepted(await sender.Send(new ReceiveCmsEventsCommand { Events = events ?? [] }, cancellationToken));

    /// <summary>
    /// Processing record of the received events, newest first. Payloads are never returned.
    /// </summary>
    /// <remarks>Administrators only: it exposes the identifiers of entities a consumer may not be allowed to see.</remarks>
    [HttpGet]
    [Authorize(Policy = Policies.ContentAdministrator)]
    [ProducesResponseType<PagingWithEnumerableList<CmsEventDTO>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagingWithEnumerableList<CmsEventDTO>>> GetPaged(
        [FromQuery] GetPagedCmsEventQuery query,
        CancellationToken cancellationToken)
            => Respond(await sender.Send(query, cancellationToken));
}
