using Lateral.CMS.Domain.Enumerations;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Requests;

public class ApplyCmsEventCommand : IRequest<IResult<CmsEventStatus>>
{
    public long CmsEventId { get; set; }
}
