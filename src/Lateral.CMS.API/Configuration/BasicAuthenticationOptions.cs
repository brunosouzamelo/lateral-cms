namespace Lateral.CMS.API.Configuration;

/// <summary>
/// Credentials accepted by the Basic scheme. Configured under <c>Authentication:Basic</c>; in a real deployment
/// the section comes from a secret store (user secrets, environment variables, Key Vault), never from the repository.
/// </summary>
public class BasicAuthenticationOptions
{
    public const string SectionName = "Authentication:Basic";

    /// <summary>Realm returned in the <c>WWW-Authenticate</c> challenge.</summary>
    public string Realm { get; set; } = "Lateral CMS";

    public List<BasicAuthenticationUserOptions> Users { get; set; } = [];
}

public class BasicAuthenticationUserOptions
{
    public string UserName { get; set; } = string.Empty;

    /// <summary>Random GUID shared with the caller.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>See <see cref="Domain.Constants.Roles"/>.</summary>
    public List<string> Roles { get; set; } = [];
}
