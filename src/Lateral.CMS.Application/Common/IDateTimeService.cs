namespace Lateral.CMS.Application.Common;

public interface IDateTimeService
{
    /// <summary>Current UTC date/time.</summary>
    DateTimeOffset UtcNowOffset { get; }
}
