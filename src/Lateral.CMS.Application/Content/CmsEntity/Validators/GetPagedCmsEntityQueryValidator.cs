using FluentValidation;
using Lateral.CMS.Application.Common.Paging;
using Lateral.CMS.Application.Content.CmsEntity.Requests;
using Lateral.CMS.Application.Ingestion.CmsEvent.Validators;

namespace Lateral.CMS.Application.Content.CmsEntity.Validators;

public class GetPagedCmsEntityQueryValidator : AbstractValidator<GetPagedCmsEntityQuery>
{
    public GetPagedCmsEntityQueryValidator()
    {
        Include(new PagingFilterValidator());

        RuleFor(q => q.Id)
            .MaximumLength(CmsEventRequestValidator.ExternalIdMaxLength)
                .WithMessage($"'id' must have up to {CmsEventRequestValidator.ExternalIdMaxLength} characters.");

        RuleFor(q => q.SortColumn).IsInEnum().WithMessage("'sortColumn' is invalid.");
        RuleFor(q => q.Status).IsInEnum().WithMessage("'status' is invalid.");
    }
}
