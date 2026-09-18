using FluentValidation;
using Lateral.CMS.Application.Content.CmsEntity.DTOs;
using Lateral.CMS.Application.Content.CmsEntity.Enumerations;
using Lateral.CMS.Application.Content.CmsEntity.Requests;
using Lateral.CMS.Application.Content.CmsEntity.Services;
using Lateral.CMS.Application.Security;
using NuvTools.Data.EntityFrameworkCore.Paging;
using NuvTools.Data.Paging;
using NuvTools.Data.Sorting;
using NuvTools.Data.Sorting.Enumerations;

namespace Lateral.CMS.Application.Content.CmsEntity.Handlers;

public class GetPagedCmsEntityQueryHandler(
    ICmsReadOnlyDbContext context,
    ICurrentUserService currentUserService,
    IValidator<GetPagedCmsEntityQuery> validator)
        : IRequestHandler<GetPagedCmsEntityQuery, IResult<PagingWithEnumerableList<CmsEntityDTO>>>
{
    public async Task<IResult<PagingWithEnumerableList<CmsEntityDTO>>> Handle(
        GetPagedCmsEntityQuery request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result<PagingWithEnumerableList<CmsEntityDTO>>.ValidationFail(validation.Errors.Select(e => e.ErrorMessage).ToList());

        var isAdmin = currentUserService.CurrentUser.IsAdmin;

        var query = context.CmsEntity.Where(CmsEntityVisibility.For(isAdmin));

        if (!string.IsNullOrWhiteSpace(request.Id))
        {
            var idPrefix = request.Id.Trim();
            query = query.Where(e => e.ExternalId.StartsWith(idPrefix));
        }

        if (isAdmin)
        {
            if (request.Status.HasValue)
                query = query.Where(e => e.CmsEntityStatusId == request.Status.Value);

            if (request.IsDisabledByAdmin.HasValue)
                query = query.Where(e => e.IsDisabledByAdmin == request.IsDisabledByAdmin.Value);
        }

        var sorted = request.SortColumn switch
        {
            OrderingCmsEntity.LastEventTimestamp => query
                .Sort(e => e.LastEventTimestamp, request.SortDirection)
                .ThenSort(e => e.ExternalId, SortDirection.ASC),
            _ => query.Sort(e => e.ExternalId, request.SortDirection)
        };

        var result = await sorted
            .Select(CmsEntityDTO.Projection)
            .PagingWrapWithEnumerableListAsync(request.PageIndex, request.PageSize, request.CountMode, cancellationToken);

        return Result<PagingWithEnumerableList<CmsEntityDTO>>.Success(result);
    }
}
