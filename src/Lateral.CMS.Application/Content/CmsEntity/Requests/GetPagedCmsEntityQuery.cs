using Lateral.CMS.Application.Content.CmsEntity.DTOs;
using Lateral.CMS.Application.Content.CmsEntity.Enumerations;
using Lateral.CMS.Domain.Enumerations;
using NuvTools.Data.Paging;

namespace Lateral.CMS.Application.Content.CmsEntity.Requests;

/// <summary>
/// Filters for the entity listing, on top of the paging and sorting the base type provides
/// (<c>pageIndex</c>, <c>pageSize</c>, <c>sortColumn</c>, <c>sortDirection</c>, <c>countMode</c>).
/// </summary>
/// <remarks>
/// A filter never widens what a caller may see: the visibility rule is applied first, so the
/// administrator-only filters below simply have nothing to match for a consumer.
/// </remarks>
public class GetPagedCmsEntityQuery : PagingFilter<OrderingCmsEntity>, IRequest<IResult<PagingWithEnumerableList<CmsEntityDTO>>>
{
    /// <summary>
    /// Returns only entities whose identifier starts with this text. Up to 128 characters; an exact
    /// identifier works as a prefix of itself.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Returns only entities the CMS currently has in this state. Administrators only — a consumer sees
    /// published entities either way, so the filter is ignored for them.
    /// </summary>
    public CmsEntityStatus? Status { get; set; }

    /// <summary>
    /// Returns only entities that are, or are not, hidden by the local admin override. Administrators
    /// only — a consumer never sees a disabled entity, so the filter is ignored for them.
    /// </summary>
    public bool? IsDisabledByAdmin { get; set; }
}
