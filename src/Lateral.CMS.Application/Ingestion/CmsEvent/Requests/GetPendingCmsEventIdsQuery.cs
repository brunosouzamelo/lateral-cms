namespace Lateral.CMS.Application.Ingestion.CmsEvent.Requests;

public class GetPendingCmsEventIdsQuery : IRequest<IResult<IList<long>>>
{
    public int Take { get; set; }
}
