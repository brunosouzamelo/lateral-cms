namespace Lateral.CMS.Application.Ingestion.CmsEvent.Requests;

public class RegisterCmsEventFailureCommand : IRequest<IResult>
{
    public long CmsEventId { get; set; }
    public required string Error { get; set; }
}
