using Lateral.CMS.Application.Common;

namespace Lateral.CMS.UnitTests.Support;

/// <summary>A clock the tests move by hand, so nothing depends on how long a test takes to run.</summary>
public class FakeDateTimeService(DateTimeOffset? utcNow = null) : IDateTimeService
{
    public DateTimeOffset UtcNowOffset { get; set; } = utcNow ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan amount) => UtcNowOffset += amount;
}
