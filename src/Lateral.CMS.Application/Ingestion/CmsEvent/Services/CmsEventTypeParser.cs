using Lateral.CMS.Domain.Enumerations;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Services;

public static class CmsEventTypeParser
{
    public const string SupportedTypes = "publish, unPublish, delete";

    /// <summary>
    /// Parses the event type sent by the CMS (case-insensitive). Numeric strings are not accepted,
    /// unlike <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>.
    /// </summary>
    public static bool TryParse(string? value, out CmsEventType type)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "publish":
                type = CmsEventType.Publish;
                return true;
            case "unpublish":
                type = CmsEventType.UnPublish;
                return true;
            case "delete":
                type = CmsEventType.Delete;
                return true;
            default:
                type = default;
                return false;
        }
    }

    public static bool IsVersioned(CmsEventType type) => type is CmsEventType.Publish or CmsEventType.UnPublish;
}
