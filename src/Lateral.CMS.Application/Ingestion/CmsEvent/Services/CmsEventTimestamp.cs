namespace Lateral.CMS.Application.Ingestion.CmsEvent.Services;

public static class CmsEventTimestamp
{
    /// <summary>
    /// Converts to UTC and truncates to microseconds, the coarsest precision the supported stores keep, so a
    /// re-delivered event compares equal to the value already persisted and is detected as a duplicate.
    /// </summary>
    public static DateTimeOffset Normalize(DateTimeOffset timestamp)
    {
        var utcTicks = timestamp.UtcTicks;
        return new DateTimeOffset(utcTicks - utcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }
}
