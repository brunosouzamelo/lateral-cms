using Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;
using Lateral.CMS.Domain.Enumerations;
using NuvTools.Data.Paging;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Requests;

public class GetPagedCmsEventQuery : PagingFilter, IRequest<IResult<PagingWithEnumerableList<CmsEventDTO>>>
{
    public CmsEventStatus? Status { get; set; }
    public Guid? BatchId { get; set; }
    public string? ExternalId { get; set; }
}
