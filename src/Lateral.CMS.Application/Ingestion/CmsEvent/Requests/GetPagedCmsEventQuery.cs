using Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;
using Lateral.CMS.Domain.Enumerations;
using NuvTools.Data.Paging;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Requests;

/// <summary>
/// Filters for the event log, on top of the paging the base type provides (<c>pageIndex</c>,
/// <c>pageSize</c>, <c>countMode</c>). Events are always returned newest first.
/// </summary>
public class GetPagedCmsEventQuery : PagingFilter, IRequest<IResult<PagingWithEnumerableList<CmsEventDTO>>>
{
    /// <summary>
    /// Returns only events that ended up in this state. <c>Failed</c> answers "what did not get through",
    /// and each row carries the reason with it.
    /// </summary>
    public CmsEventStatus? Status { get; set; }

    /// <summary>
    /// Returns only the events of one delivery, using the identifier the webhook answered with.
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>
    /// Returns only the events about one entity, matched exactly. This is the history of that entity as
    /// the CMS reported it, including the deliveries that changed nothing.
    /// </summary>
    public string? ExternalId { get; set; }
}
