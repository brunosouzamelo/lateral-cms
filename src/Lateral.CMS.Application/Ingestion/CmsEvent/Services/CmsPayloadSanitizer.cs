using System.Buffers;
using System.Text;
using System.Text.Json;
using Lateral.CMS.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Services;

public readonly record struct CmsPayloadSanitizationResult(bool Succeeded, string? Payload, string? Error);

/// <summary>
/// Validates and normalizes an event payload before it is stored. The payload is rebuilt node by node:
/// <list type="bullet">
/// <item>must be a JSON object within the configured size and depth;</item>
/// <item>duplicated property names are rejected (ambiguous: parsers keep different occurrences);</item>
/// <item>control characters other than tab/CR/LF are removed from names and strings (NUL is also rejected by several JSON stores);</item>
/// <item>strings with invalid Unicode (lone surrogates) are rejected;</item>
/// <item>output is compact and HTML-sensitive characters are escaped, so consumers can embed it safely.</item>
/// </list>
/// </summary>
public class CmsPayloadSanitizer(IOptions<IngestionOptions> options)
{
    private const int MaxReportedNameLength = 50;

    public CmsPayloadSanitizationResult Sanitize(JsonElement payload)
    {
        var settings = options.Value;

        if (payload.ValueKind != JsonValueKind.Object)
            return Fail("'payload' must be a JSON object.");

        var buffer = new ArrayBufferWriter<byte>();

        try
        {
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = settings.MaxPayloadDepth + 1 }))
            {
                WriteElement(writer, payload, 1, settings);
            }
        }
        catch (PayloadRejectedException ex)
        {
            return Fail(ex.Message);
        }
        catch (InvalidOperationException)
        {
            // JsonElement.GetString / property names throw when the text holds invalid UTF-16 (lone surrogates).
            return Fail("'payload' contains invalid Unicode text.");
        }

        if (buffer.WrittenCount > settings.MaxPayloadBytes)
            return Fail($"'payload' exceeds the maximum size of {settings.MaxPayloadBytes} bytes.");

        return new CmsPayloadSanitizationResult(true, Encoding.UTF8.GetString(buffer.WrittenSpan), null);
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element, int depth, IngestionOptions settings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                EnsureDepth(depth, settings);
                writer.WriteStartObject();

                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var name = RemoveControlCharacters(property.Name);
                    if (!names.Add(name))
                        throw new PayloadRejectedException($"'payload' has the duplicated property '{Shorten(name)}'.");

                    writer.WritePropertyName(name);
                    WriteElement(writer, property.Value, depth + 1, settings);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                EnsureDepth(depth, settings);
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                    WriteElement(writer, item, depth + 1, settings);

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(RemoveControlCharacters(element.GetString()!));
                break;

            case JsonValueKind.Number:
                // Raw text keeps the exact precision sent by the CMS (no double/decimal round-trip).
                writer.WriteRawValue(element.GetRawText());
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new PayloadRejectedException("'payload' contains an unsupported JSON value.");
        }
    }

    private static void EnsureDepth(int depth, IngestionOptions settings)
    {
        if (depth > settings.MaxPayloadDepth)
            throw new PayloadRejectedException($"'payload' exceeds the maximum nesting depth of {settings.MaxPayloadDepth}.");
    }

    private static string RemoveControlCharacters(string value)
    {
        if (!value.Any(IsDisallowedControl))
            return value;

        return new string([.. value.Where(c => !IsDisallowedControl(c))]);
    }

    private static bool IsDisallowedControl(char c) => char.IsControl(c) && c is not ('\t' or '\n' or '\r');

    private static string Shorten(string value)
        => value.Length <= MaxReportedNameLength ? value : value[..MaxReportedNameLength] + "...";

    private static CmsPayloadSanitizationResult Fail(string error) => new(false, null, error);

    private sealed class PayloadRejectedException(string message) : Exception(message);
}
