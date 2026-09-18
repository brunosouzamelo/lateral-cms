using Lateral.CMS.Application.Content.CmsEntity.DTOs;

namespace Lateral.CMS.Application.Content.CmsEntity.Requests;

public class GetByCmsEntityIdQuery : IRequest<IResult<CmsEntityDTO>>
{
    public required string Id { get; set; }
}
