namespace Lateral.CMS.Application.Configuration;

public class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Maximum number of events accepted in one webhook call.</summary>
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>Maximum size of a single event payload, in UTF-8 bytes.</summary>
    public int MaxPayloadBytes { get; set; } = 256 * 1024;

    /// <summary>Maximum nesting depth of a payload.</summary>
    public int MaxPayloadDepth { get; set; } = 32;

    /// <summary>How far in the future an event timestamp may be (clock skew between CMS and service).</summary>
    public TimeSpan AllowedClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Runs the in-process background processor. Disable to process events from another host.</summary>
    public bool ProcessInBackground { get; set; } = true;

    /// <summary>Events loaded per processing pass.</summary>
    public int ProcessingBatchSize { get; set; } = 200;

    /// <summary>Fallback polling interval when no new batch signal arrives (e.g. events left by a restart).</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Attempts before an event is marked as <c>Failed</c>.</summary>
    public int MaxProcessingAttempts { get; set; } = 5;

    /// <summary>Base delay of the exponential retry back-off.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
