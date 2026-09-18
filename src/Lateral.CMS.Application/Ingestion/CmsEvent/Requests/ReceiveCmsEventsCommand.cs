using Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Requests;

public class ReceiveCmsEventsCommand : IRequest<IResult<CmsEventBatchReceiptDTO>>
{
    public List<CmsEventRequest?> Events { get; set; } = [];
}
