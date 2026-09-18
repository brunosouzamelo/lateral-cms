using FluentValidation;
using Lateral.CMS.Application.Common.Paging;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Validators;

public class GetPagedCmsEventQueryValidator : AbstractValidator<GetPagedCmsEventQuery>
{
    public GetPagedCmsEventQueryValidator()
    {
        Include(new PagingFilterValidator());
    }
}
