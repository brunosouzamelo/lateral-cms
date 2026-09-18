using FluentValidation;
using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Validators;

public class ReceiveCmsEventsCommandValidator : AbstractValidator<ReceiveCmsEventsCommand>
{
    public ReceiveCmsEventsCommandValidator(IOptions<IngestionOptions> options)
    {
        var maxBatchSize = options.Value.MaxBatchSize;

        RuleFor(c => c.Events)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("The batch must contain at least one event.")
            .Must(events => events.Count <= maxBatchSize)
                .WithMessage($"The batch must contain up to {maxBatchSize} events.");
    }
}
