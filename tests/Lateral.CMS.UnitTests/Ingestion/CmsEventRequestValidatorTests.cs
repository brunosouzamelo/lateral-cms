using System.Text.Json;
using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Lateral.CMS.Application.Ingestion.CmsEvent.Validators;
using Lateral.CMS.UnitTests.Support;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.UnitTests.Ingestion;

/// <summary>The constraints an event has to satisfy before it is accepted into the inbox.</summary>
public class CmsEventRequestValidatorTests
{
    private readonly FakeDateTimeService _clock = new();

    [Fact]
    public void Validate_Accepts_ACompletePublishEvent()
        => Assert.Empty(Validate(Publish()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("created")]
    [InlineData("1")]
    public void Validate_Rejects_AMissingOrUnsupportedType(string? type)
    {
        var errors = Validate(Publish(request => request.Type = type));

        Assert.Contains(errors, error => error.Contains("'type'"));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("Publish")]
    [InlineData("unPublish")]
    [InlineData("UNPUBLISH")]
    [InlineData("delete")]
    public void Validate_Accepts_TheSupportedTypes_RegardlessOfCase(string type)
    {
        var errors = Validate(Publish(request => request.Type = type));

        Assert.DoesNotContain(errors, error => error.Contains("'type'"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    public void Validate_Rejects_AMissingOrMalformedId(string? id)
    {
        var errors = Validate(Publish(request => request.Id = id));

        Assert.Contains(errors, error => error.Contains("'id'"));
    }

    [Fact]
    public void Validate_Rejects_AnIdOverTheMaximumLength()
    {
        var errors = Validate(Publish(request => request.Id = new string('x', CmsEventRequestValidator.ExternalIdMaxLength + 1)));

        Assert.Contains(errors, error => error.Contains("'id'"));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("unPublish")]
    public void Validate_Rejects_AVersionedEventWithoutAVersion(string type)
    {
        var errors = Validate(Publish(request =>
        {
            request.Type = type;
            request.Version = null;
        }));

        Assert.Contains(errors, error => error.Contains("'version'"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Rejects_AVersionBelowTheFirstOne(int version)
    {
        var errors = Validate(Publish(request => request.Version = version));

        Assert.Contains(errors, error => error.Contains("'version'"));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("unPublish")]
    public void Validate_Rejects_AVersionedEventWithoutAPayload(string type)
    {
        var errors = Validate(Publish(request =>
        {
            request.Type = type;
            request.Payload = null;
        }));

        Assert.Contains(errors, error => error.Contains("'payload'"));
    }

    [Fact]
    public void Validate_Accepts_ADeleteWithoutVersionOrPayload()
    {
        var errors = Validate(new CmsEventRequest
        {
            Type = "delete",
            Id = "entity-1",
            Timestamp = _clock.UtcNowOffset.AddMinutes(-1)
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Rejects_AMissingTimestamp()
    {
        var errors = Validate(Publish(request => request.Timestamp = null));

        Assert.Contains(errors, error => error.Contains("'timestamp'"));
    }

    [Fact]
    public void Validate_Rejects_ATimestampFurtherInTheFutureThanTheAllowedClockSkew()
    {
        var errors = Validate(Publish(request => request.Timestamp = _clock.UtcNowOffset.AddHours(1)));

        Assert.Contains(errors, error => error.Contains("'timestamp'"));
    }

    [Fact]
    public void Validate_Accepts_ATimestampWithinTheAllowedClockSkew()
    {
        // The CMS clock may be slightly ahead; that is not a reason to drop an event.
        var errors = Validate(Publish(request => request.Timestamp = _clock.UtcNowOffset.AddMinutes(1)));

        Assert.Empty(errors);
    }

    private CmsEventRequest Publish(Action<CmsEventRequest>? customize = null)
    {
        var request = new CmsEventRequest
        {
            Type = "publish",
            Id = "entity-1",
            Version = 1,
            Payload = JsonDocument.Parse("""{"title":"A book"}""").RootElement.Clone(),
            Timestamp = _clock.UtcNowOffset.AddMinutes(-1)
        };

        customize?.Invoke(request);

        return request;
    }

    private List<string> Validate(CmsEventRequest request)
    {
        var validator = new CmsEventRequestValidator(Options.Create(new IngestionOptions()), _clock);

        return [.. validator.Validate(request).Errors.Select(error => error.ErrorMessage)];
    }
}
