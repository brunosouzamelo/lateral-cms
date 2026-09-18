using Lateral.CMS.Application.Content.CmsEntity.DTOs;
using Lateral.CMS.Application.Content.CmsEntity.Requests;
using Lateral.CMS.Application.Content.CmsEntity.Services;
using Lateral.CMS.Application.Security;

namespace Lateral.CMS.Application.Content.CmsEntity.Handlers;

public class GetByCmsEntityIdQueryHandler(ICmsReadOnlyDbContext context, ICurrentUserService currentUserService)
        : IRequestHandler<GetByCmsEntityIdQuery, IResult<CmsEntityDTO>>
{
    public async Task<IResult<CmsEntityDTO>> Handle(
        GetByCmsEntityIdQuery request,
        CancellationToken cancellationToken)
    {
        var id = request.Id.Trim();

        var result = await context.CmsEntity
            .Where(CmsEntityVisibility.For(currentUserService.CurrentUser.IsAdmin))
            .Where(e => e.ExternalId == id)
            .Select(CmsEntityDTO.Projection)
            .FirstOrDefaultAsync(cancellationToken);

        // Not visible and not existing answer the same way, so users cannot probe for disabled entities.
        if (result is null)
            return Result<CmsEntityDTO>.FailNotFound($"Entity '{id}' was not found.");

        return Result<CmsEntityDTO>.Success(result);
    }
}
