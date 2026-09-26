using System.Text;
using System.Text.Json;
using Lateral.CMS.Application.Configuration;
using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.UnitTests.Ingestion;

/// <summary>Sanitizing of the payload, which is the only part of an event that is free-form.</summary>
public class CmsPayloadSanitizerTests
{
    [Fact]
    public void Sanitize_KeepsTheContent_AndReturnsCompactJson()
    {
        var result = Sanitize("""{ "title" : "A book" , "tags" : [ "a" , "b" ] }""");

        Assert.True(result.Succeeded);
        Assert.Equal("""{"title":"A book","tags":["a","b"]}""", result.Payload);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void Sanitize_Rejects_WhatIsNotAJsonObject(string payload)
    {
        var result = Sanitize(payload);

        Assert.False(result.Succeeded);
        Assert.Contains("must be a JSON object", result.Error);
    }

    [Fact]
    public void Sanitize_RemovesControlCharacters_FromValuesAndPropertyNames()
    {
        var result = Sanitize("{ \"ti\\u0000tle\": \"a\\u0007b\" }");

        Assert.True(result.Succeeded);
        Assert.Equal("""{"title":"ab"}""", result.Payload);
    }

    [Fact]
    public void Sanitize_KeepsTabsAndNewLines_WhichAreLegitimateInText()
    {
        var result = Sanitize("""{ "body": "line\nnext\ttab" }""");

        Assert.True(result.Succeeded);
        Assert.Equal("""{"body":"line\nnext\ttab"}""", result.Payload);
    }

    [Fact]
    public void Sanitize_Rejects_DuplicatedPropertyNames()
    {
        // Ambiguous: JSON parsers disagree on which occurrence wins, so the event is not stored at all.
        var result = Sanitize("""{ "title": "first", "title": "second" }""");

        Assert.False(result.Succeeded);
        Assert.Contains("duplicated property", result.Error);
    }

    [Fact]
    public void Sanitize_Rejects_APayloadOverTheSizeLimit()
    {
        var result = Sanitize($$"""{ "body": "{{new string('x', 200)}}" }""", new IngestionOptions { MaxPayloadBytes = 64 });

        Assert.False(result.Succeeded);
        Assert.Contains("maximum size", result.Error);
    }

    [Fact]
    public void Sanitize_Rejects_APayloadNestedDeeperThanTheLimit()
    {
        var deep = new StringBuilder("{");
        for (var level = 0; level < 10; level++)
            deep.Append($"\"level{level}\":{{");
        deep.Append('}', 10).Append('}');

        var result = Sanitize(deep.ToString(), new IngestionOptions { MaxPayloadDepth = 5 });

        Assert.False(result.Succeeded);
        Assert.Contains("nesting depth", result.Error);
    }

    [Fact]
    public void Sanitize_KeepsNumbersExactly_WithoutRoundingThem()
    {
        var result = Sanitize("""{ "price": 12345678901234567890.123456789 }""");

        Assert.True(result.Succeeded);
        Assert.Equal("""{"price":12345678901234567890.123456789}""", result.Payload);
    }

    [Fact]
    public void Sanitize_EscapesHtmlSensitiveCharacters_SoConsumersCanEmbedThePayload()
    {
        var result = Sanitize("""{ "body": "<script>alert('x')</script>" }""");

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("<script>", result.Payload);
    }

    private static CmsPayloadSanitizationResult Sanitize(string payload, IngestionOptions? settings = null)
    {
        using var document = JsonDocument.Parse(payload);

        var sanitizer = new CmsPayloadSanitizer(Options.Create(settings ?? new IngestionOptions()));

        return sanitizer.Sanitize(document.RootElement);
    }
}
