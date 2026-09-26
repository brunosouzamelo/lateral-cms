namespace Lateral.CMS.Simulator;

/// <summary>
/// Builds the event shapes the CMS puts on the wire. Deliberately loose — <see cref="Malformed"/> exists
/// so a run can show what the webhook does with an event it cannot accept.
/// </summary>
public static class CmsEvents
{
    private static readonly string[] Titles =
    [
        "Quarterly report", "Release notes", "Pricing update", "Team page", "Security advisory",
        "Case study", "Changelog", "Getting started", "Migration guide", "Roadmap"
    ];

    private static readonly string[] Locales = ["en-GB", "en-US", "pt-BR", "de-DE"];

    /// <summary>A version was made public. Carries the entity fields at that version.</summary>
    public static object Publish(string id, int version, DateTimeOffset timestamp, object? payload = null)
        => new { type = "publish", id, version = (int?)version, payload = payload ?? Payload(id, version), timestamp };

    /// <summary>
    /// A version was withdrawn. Carries the fields too, so a version that was never published still
    /// reaches the service.
    /// </summary>
    public static object UnPublish(string id, int version, DateTimeOffset timestamp, object? payload = null)
        => new { type = "unPublish", id, version = (int?)version, payload = payload ?? Payload(id, version), timestamp };

    /// <summary>The entity was removed. No version, no payload.</summary>
    public static object Delete(string id, DateTimeOffset timestamp)
        => new { type = "delete", id, version = (int?)null, payload = (object?)null, timestamp };

    /// <summary>An event the webhook should refuse, for showing what the receipt reports.</summary>
    public static object Malformed(string reason, string? id, DateTimeOffset timestamp) => reason switch
    {
        "no-version" => new { type = "publish", id, version = (int?)null, payload = (object?)Payload(id ?? "x", 1), timestamp },
        "bad-type" => new { type = "archive", id, version = (int?)1, payload = (object?)Payload(id ?? "x", 1), timestamp },
        "no-payload" => new { type = "publish", id, version = (int?)1, payload = (object?)null, timestamp },
        "payload-not-object" => new { type = "publish", id, version = (int?)1, payload = (object?)"a string", timestamp },
        "future" => new { type = "publish", id, version = (int?)1, payload = (object?)Payload(id ?? "x", 1), timestamp = DateTimeOffset.UtcNow.AddDays(1) },
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown malformed event.")
    };

    /// <summary>Content that looks like something a CMS would hold, so payloads are not all identical.</summary>
    public static object Payload(string id, int version, Random? random = null)
    {
        var index = Math.Abs(id.GetHashCode(StringComparison.Ordinal));

        return new
        {
            title = $"{Titles[index % Titles.Length]} (v{version})",
            slug = id.ToLowerInvariant(),
            locale = Locales[index % Locales.Length],
            body = $"Body of {id} at version {version}.",
            tags = new[] { "simulated", $"v{version}" },
            wordCount = random?.Next(200, 4000) ?? 800 + version,
            updatedBy = "cms-editor@example.com"
        };
    }
}
