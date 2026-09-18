namespace Lateral.CMS.Application.Common;

public class DateTimeService(TimeProvider timeProvider) : IDateTimeService
{
    public DateTimeOffset UtcNowOffset => timeProvider.GetUtcNow();
}
