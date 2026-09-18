using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lateral.CMS.Application.Common.Serialization;

/// <summary>
/// Writes a string that already holds JSON as a JSON value (not as an escaped string), so stored payloads
/// are returned without being parsed and re-serialized on every read.
/// </summary>
public class RawJsonStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteRawValue(value);
}
