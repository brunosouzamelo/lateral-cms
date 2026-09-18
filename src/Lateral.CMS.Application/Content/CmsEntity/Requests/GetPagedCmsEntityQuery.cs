using Lateral.CMS.Application.Content.CmsEntity.DTOs;
using Lateral.CMS.Application.Content.CmsEntity.Enumerations;
using Lateral.CMS.Domain.Enumerations;
using NuvTools.Data.Paging;

namespace Lateral.CMS.Application.Content.CmsEntity.Requests;

public class GetPagedCmsEntityQuery : PagingFilter<OrderingCmsEntity>, IRequest<IResult<PagingWithEnumerableList<CmsEntityDTO>>>
{
    /// <summary>Filters by identifier prefix.</summary>
    public string? Id { get; set; }

    /// <summary>Admin only (ignored for users, who only see published entities).</summary>
    public CmsEntityStatus? Status { get; set; }

    /// <summary>Admin only (ignored for users, who never see disabled entities).</summary>
    public bool? IsDisabledByAdmin { get; set; }
}
