using FluentValidation;
using Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using NuvTools.Data.EntityFrameworkCore.Paging;
using NuvTools.Data.Paging;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Handlers;

public class GetPagedCmsEventQueryHandler(ICmsReadOnlyDbContext context, IValidator<GetPagedCmsEventQuery> validator)
        : IRequestHandler<GetPagedCmsEventQuery, IResult<PagingWithEnumerableList<CmsEventDTO>>>
{
    public async Task<IResult<PagingWithEnumerableList<CmsEventDTO>>> Handle(
        GetPagedCmsEventQuery request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result<PagingWithEnumerableList<CmsEventDTO>>.ValidationFail(validation.Errors.Select(e => e.ErrorMessage).ToList());

        var query = context.CmsEvent;

        if (request.Status.HasValue)
            query = query.Where(e => e.CmsEventStatusId == request.Status.Value);

        if (request.BatchId.HasValue)
            query = query.Where(e => e.BatchId == request.BatchId.Value);

        if (!string.IsNullOrWhiteSpace(request.ExternalId))
        {
            var externalId = request.ExternalId.Trim();
            query = query.Where(e => e.ExternalId == externalId);
        }

        var result = await query
            .OrderByDescending(e => e.CmsEventId)
            .Select(CmsEventDTO.Projection)
            .PagingWrapWithEnumerableListAsync(request.PageIndex, request.PageSize, request.CountMode, cancellationToken);

        return Result<PagingWithEnumerableList<CmsEventDTO>>.Success(result);
    }
}
