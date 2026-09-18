using Lateral.CMS.Application.Content.CmsEntity.Requests;
using Lateral.CMS.Application.Security;

namespace Lateral.CMS.Application.Content.CmsEntity.Handlers;

public class SetCmsEntityDisabledCommandHandler(
    IContentDbContext context,
    ICurrentUserService currentUserService,
    IDateTimeService dateTimeService,
    ILogger<SetCmsEntityDisabledCommandHandler> logger)
        : IRequestHandler<SetCmsEntityDisabledCommand, IResult>
{
    public async Task<IResult> Handle(
        SetCmsEntityDisabledCommand request,
        CancellationToken cancellationToken)
    {
        var id = request.Id.Trim();
        var entity = await context.CmsEntity.FirstOrDefaultAsync(e => e.ExternalId == id, cancellationToken);

        if (entity is null)
            return Result.FailNotFound($"Entity '{id}' was not found.");

        if (entity.IsDisabledByAdmin == request.IsDisabled)
            return Result.Success();

        var userName = currentUserService.CurrentUser.UserName;

        entity.IsDisabledByAdmin = request.IsDisabled;
        entity.DisabledByAdminDate = request.IsDisabled ? dateTimeService.UtcNowOffset : null;
        entity.DisabledByAdminUser = request.IsDisabled ? userName : null;
        entity.ConcurrencyToken = Guid.NewGuid();

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Entity {ExternalId} {Action} by admin {UserName}.",
            id, request.IsDisabled ? "disabled" : "enabled", userName);

        return Result.Success();
    }
}
