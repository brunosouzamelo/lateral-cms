using FluentValidation;
using NuvTools.Data.Paging;

namespace Lateral.CMS.Application.Common.Paging;

public class PagingFilterValidator : AbstractValidator<PagingFilter>
{
    public const int MaxPageSize = 100;

    public PagingFilterValidator()
    {
        RuleFor(p => p.PageIndex)
            .GreaterThanOrEqualTo(0).WithMessage("'pageIndex' must be greater than or equal to 0.");

        RuleFor(p => p.PageSize)
            .InclusiveBetween(1, MaxPageSize).WithMessage($"'pageSize' must be between 1 and {MaxPageSize}.");
    }
}
