using System.Text.Json;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Requests;

/// <summary>
/// One event of a webhook batch, as sent by the CMS. Every member is nullable so an incomplete event is
/// rejected on its own by validation instead of failing the whole batch at model binding.
/// </summary>
public class CmsEventRequest
{
    /// <summary>
    /// What happened to the entity: <c>publish</c>, <c>unPublish</c> or <c>delete</c>. Case is ignored.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Identifier the CMS assigned to the entity. Up to 128 characters, without whitespace or control
    /// characters. It is what ties every event for the same entity together.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// The entity fields at this version, as a JSON object. Required for <c>publish</c> and
    /// <c>unPublish</c> — an <c>unPublish</c> carries them too, so the version being withdrawn is stored
    /// even when it was never published. Not read for <c>delete</c>.
    /// </summary>
    /// <remarks>
    /// Sanitized before it is stored: size and nesting are capped, duplicated property names are refused
    /// as ambiguous, and control characters are stripped.
    /// </remarks>
    public JsonElement? Payload { get; set; }

    /// <summary>
    /// Version of the entity this event carries, starting at 1. Required for <c>publish</c> and
    /// <c>unPublish</c>; a <c>delete</c> has none. The version decides which of two events is newer.
    /// </summary>
    public int? Version { get; set; }

    /// <summary>
    /// When the event happened in the CMS (UTC). It breaks the tie between two events of the same version
    /// and, after a deletion, decides whether a late event is discarded.
    /// </summary>
    /// <remarks>
    /// May not be further in the future than the configured clock skew, which leaves room for the CMS
    /// clock to run slightly ahead.
    /// </remarks>
    public DateTimeOffset? Timestamp { get; set; }
}
