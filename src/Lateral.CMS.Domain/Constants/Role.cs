namespace Lateral.CMS.Domain.Constants;

public static class Roles
{
    /// <summary>The CMS itself (organization request): may only push events to the webhook.</summary>
    public const string Organization = "Organization";

    /// <summary>Consumer: reads published entities that are not disabled.</summary>
    public const string User = "User";

    /// <summary>Consumer with full visibility, allowed to disable/enable entities locally.</summary>
    public const string Admin = "Admin";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Organization, User, Admin };
}
