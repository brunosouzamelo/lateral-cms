using System.Text.Json;
using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Requests;
using Lateral.CMS.Application.Ingestion.CmsEvent.Validators;
using Lateral.CMS.UnitTests.Support;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.UnitTests.Ingestion;

/// <summary>The constraints an event has to satisfy before it is accepted into the inbox.</summary>
[TestFixture]
public class CmsEventRequestValidatorTests
{
    private readonly FakeDateTimeService _clock = new();

    [Test]
    public void Validate_Accepts_ACompletePublishEvent()
        => Assert.That(Validate(Publish()), Is.Empty);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("created")]
    [TestCase("1")]
    public void Validate_Rejects_AMissingOrUnsupportedType(string? type)
        => Assert.That(Errors(Publish(request => request.Type = type)), Does.Contain("'type'"));

    [TestCase("publish")]
    [TestCase("Publish")]
    [TestCase("unPublish")]
    [TestCase("UNPUBLISH")]
    [TestCase("delete")]
    public void Validate_Accepts_TheSupportedTypes_RegardlessOfCase(string type)
        => Assert.That(Errors(Publish(request => request.Type = type)), Does.Not.Contain("'type'"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("has space")]
    public void Validate_Rejects_AMissingOrMalformedId(string? id)
        => Assert.That(Errors(Publish(request => request.Id = id)), Does.Contain("'id'"));

    [Test]
    public void Validate_Rejects_AnIdOverTheMaximumLength()
    {
        var errors = Errors(Publish(request => request.Id = new string('x', CmsEventRequestValidator.ExternalIdMaxLength + 1)));

        Assert.That(errors, Does.Contain("'id'"));
    }

    [TestCase("publish")]
    [TestCase("unPublish")]
    public void Validate_Rejects_AVersionedEventWithoutAVersion(string type)
    {
        var errors = Errors(Publish(request =>
        {
            request.Type = type;
            request.Version = null;
        }));

        Assert.That(errors, Does.Contain("'version'"));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Validate_Rejects_AVersionBelowTheFirstOne(int version)
        => Assert.That(Errors(Publish(request => request.Version = version)), Does.Contain("'version'"));

    [TestCase("publish")]
    [TestCase("unPublish")]
    public void Validate_Rejects_AVersionedEventWithoutAPayload(string type)
    {
        var errors = Errors(Publish(request =>
        {
            request.Type = type;
            request.Payload = null;
        }));

        Assert.That(errors, Does.Contain("'payload'"));
    }

    [Test]
    public void Validate_Accepts_ADeleteWithoutVersionOrPayload()
    {
        var errors = Validate(new CmsEventRequest
        {
            Type = "delete",
            Id = "entity-1",
            Timestamp = _clock.UtcNowOffset.AddMinutes(-1)
        });

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Validate_Rejects_AMissingTimestamp()
        => Assert.That(Errors(Publish(request => request.Timestamp = null)), Does.Contain("'timestamp'"));

    [Test]
    public void Validate_Rejects_ATimestampFurtherInTheFutureThanTheAllowedClockSkew()
        => Assert.That(Errors(Publish(request => request.Timestamp = _clock.UtcNowOffset.AddHours(1))), Does.Contain("'timestamp'"));

    [Test]
    public void Validate_Accepts_ATimestampWithinTheAllowedClockSkew()
    {
        // The CMS clock may be slightly ahead; that is not a reason to drop an event.
        var errors = Validate(Publish(request => request.Timestamp = _clock.UtcNowOffset.AddMinutes(1)));

        Assert.That(errors, Is.Empty);
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

    /// <summary>The messages as one string, so an assertion reports every error when it fails.</summary>
    private string Errors(CmsEventRequest request) => string.Join(" | ", Validate(request));
}
