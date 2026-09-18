using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Lateral.CMS.Domain.Enumerations;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Handlers;

/// <summary>
/// Pending events ready to run, oldest CMS timestamp first (arrival order breaks ties). Reads from the writer
/// context on purpose: a lagging replica could hide events that were just received.
/// </summary>
public class GetPendingCmsEventIdsQueryHandler(IIngestionDbContext context, IDateTimeService dateTimeService)
        : IRequestHandler<GetPendingCmsEventIdsQuery, IResult<IList<long>>>
{
    public async Task<IResult<IList<long>>> Handle(
        GetPendingCmsEventIdsQuery request,
        CancellationToken cancellationToken)
    {
        var now = dateTimeService.UtcNowOffset;

        var ids = await context.CmsEvent
            .AsNoTracking()
            .Where(e => e.CmsEventStatusId == CmsEventStatus.Pending
                && (e.NextAttemptDate == null || e.NextAttemptDate <= now))
            .OrderBy(e => e.EventTimestamp)
            .ThenBy(e => e.CmsEventId)
            .Select(e => e.CmsEventId)
            .Take(request.Take)
            .ToListAsync(cancellationToken);

        return Result<IList<long>>.Success(ids);
    }
}
