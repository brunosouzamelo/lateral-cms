using System.Text.Json;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Requests;

/// <summary>
/// One event of a webhook batch, as sent by the CMS. Every member is nullable so an incomplete event is
/// rejected on its own by validation instead of failing the whole batch at model binding.
/// </summary>
public class CmsEventRequest
{
    public string? Type { get; set; }
    public string? Id { get; set; }
    public JsonElement? Payload { get; set; }
    public int? Version { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
