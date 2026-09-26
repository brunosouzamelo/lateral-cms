using System.Text;
using System.Text.Json;
using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.UnitTests.Ingestion;

/// <summary>Sanitizing of the payload, which is the only part of an event that is free-form.</summary>
[TestFixture]
public class CmsPayloadSanitizerTests
{
    [Test]
    public void Sanitize_KeepsTheContent_AndReturnsCompactJson()
    {
        var result = Sanitize("""{ "title" : "A book" , "tags" : [ "a" , "b" ] }""");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Payload, Is.EqualTo("""{"title":"A book","tags":["a","b"]}"""));
    }

    [TestCase("[]")]
    [TestCase("\"text\"")]
    [TestCase("42")]
    [TestCase("null")]
    public void Sanitize_Rejects_WhatIsNotAJsonObject(string payload)
    {
        var result = Sanitize(payload);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Error, Does.Contain("must be a JSON object"));
    }

    [Test]
    public void Sanitize_RemovesControlCharacters_FromValuesAndPropertyNames()
    {
        var result = Sanitize("{ \"ti\\u0000tle\": \"a\\u0007b\" }");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Payload, Is.EqualTo("""{"title":"ab"}"""));
    }

    [Test]
    public void Sanitize_KeepsTabsAndNewLines_WhichAreLegitimateInText()
    {
        var result = Sanitize("""{ "body": "line\nnext\ttab" }""");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Payload, Is.EqualTo("""{"body":"line\nnext\ttab"}"""));
    }

    [Test]
    public void Sanitize_Rejects_DuplicatedPropertyNames()
    {
        // Ambiguous: JSON parsers disagree on which occurrence wins, so the event is not stored at all.
        var result = Sanitize("""{ "title": "first", "title": "second" }""");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Error, Does.Contain("duplicated property"));
    }

    [Test]
    public void Sanitize_Rejects_APayloadOverTheSizeLimit()
    {
        var result = Sanitize($$"""{ "body": "{{new string('x', 200)}}" }""", new IngestionOptions { MaxPayloadBytes = 64 });

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Error, Does.Contain("maximum size"));
    }

    [Test]
    public void Sanitize_Rejects_APayloadNestedDeeperThanTheLimit()
    {
        var deep = new StringBuilder("{");
        for (var level = 0; level < 10; level++)
            deep.Append($"\"level{level}\":{{");
        deep.Append('}', 10).Append('}');

        var result = Sanitize(deep.ToString(), new IngestionOptions { MaxPayloadDepth = 5 });

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Error, Does.Contain("nesting depth"));
    }

    [Test]
    public void Sanitize_KeepsNumbersExactly_WithoutRoundingThem()
    {
        var result = Sanitize("""{ "price": 12345678901234567890.123456789 }""");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Payload, Is.EqualTo("""{"price":12345678901234567890.123456789}"""));
    }

    [Test]
    public void Sanitize_EscapesHtmlSensitiveCharacters_SoConsumersCanEmbedThePayload()
    {
        var result = Sanitize("""{ "body": "<script>alert('x')</script>" }""");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Payload, Does.Not.Contain("<script>"));
    }

    private static CmsPayloadSanitizationResult Sanitize(string payload, IngestionOptions? settings = null)
    {
        using var document = JsonDocument.Parse(payload);

        var sanitizer = new CmsPayloadSanitizer(Options.Create(settings ?? new IngestionOptions()));

        return sanitizer.Sanitize(document.RootElement);
    }
}
