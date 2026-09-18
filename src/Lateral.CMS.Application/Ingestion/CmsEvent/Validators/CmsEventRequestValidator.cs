using FluentValidation;
using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Validators;

public class CmsEventRequestValidator : AbstractValidator<CmsEventRequest>
{
    public const int ExternalIdMaxLength = 128;

    private static readonly DateTimeOffset MinTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public CmsEventRequestValidator(IOptions<IngestionOptions> options, IDateTimeService dateTimeService)
    {
        var settings = options.Value;

        RuleFor(e => e.Type)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("'type' is required.")
            .Must(type => CmsEventTypeParser.TryParse(type, out _))
                .WithMessage($"'type' must be one of: {CmsEventTypeParser.SupportedTypes}.");

        RuleFor(e => e.Id)
            .Cascade(CascadeMode.Stop)
            .Must(id => !string.IsNullOrWhiteSpace(id)).WithMessage("'id' is required.")
            .Must(id => id!.Trim().Length <= ExternalIdMaxLength)
                .WithMessage($"'id' must have up to {ExternalIdMaxLength} characters.")
            .Must(id => !id!.Trim().Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
                .WithMessage("'id' must not contain whitespace or control characters.");

        When(e => CmsEventTypeParser.TryParse(e.Type, out var type) && CmsEventTypeParser.IsVersioned(type), () =>
        {
            RuleFor(e => e.Version)
                .Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("'version' is required for publish and unPublish events.")
                .GreaterThanOrEqualTo(1).WithMessage("'version' must be greater than or equal to 1.");

            RuleFor(e => e.Payload)
                .Cascade(CascadeMode.Stop)
                .Must(payload => payload.HasValue && payload.Value.ValueKind != JsonValueKind.Null && payload.Value.ValueKind != JsonValueKind.Undefined)
                    .WithMessage("'payload' is required for publish and unPublish events.")
                .Must(payload => payload!.Value.ValueKind == JsonValueKind.Object)
                    .WithMessage("'payload' must be a JSON object.");
        });

        RuleFor(e => e.Timestamp)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("'timestamp' is required.")
            .Must(timestamp => timestamp >= MinTimestamp)
                .WithMessage($"'timestamp' must be later than {MinTimestamp:yyyy-MM-dd}.")
            .Must(timestamp => timestamp <= dateTimeService.UtcNowOffset + settings.AllowedClockSkew)
                .WithMessage("'timestamp' must not be in the future.");
    }
}
