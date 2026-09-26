using System.Security.Cryptography;
using System.Text;
using Lateral.CMS.API.Configuration;
using Lateral.CMS.Domain.Constants;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.API.Security;

/// <summary>
/// The users accepted by the Basic scheme, read once from configuration.
/// </summary>
/// <remarks>
/// Passwords are compared in constant time and an unknown user still pays for a comparison, so neither the
/// password nor the existence of a user can be inferred from how long a request takes.
/// </remarks>
public class BasicAuthenticationCredentialStore
{
    private readonly Dictionary<string, BasicAuthenticationUserOptions> _users;
    private readonly byte[] _decoyPassword = RandomNumberGenerator.GetBytes(32);

    public BasicAuthenticationCredentialStore(IOptions<BasicAuthenticationOptions> options)
    {
        var settings = options.Value;

        Realm = string.IsNullOrWhiteSpace(settings.Realm) ? "Lateral CMS" : settings.Realm;

        // User names are an identifier, not text: compared ordinally, but case is not meaningful.
        _users = settings.Users
            .Where(user => !string.IsNullOrWhiteSpace(user.UserName))
            .ToDictionary(user => user.UserName, StringComparer.OrdinalIgnoreCase);
    }

    public string Realm { get; }

    /// <summary>
    /// Validates the pair and returns the roles of the user, or <c>null</c> when the pair is not accepted.
    /// </summary>
    public IReadOnlyCollection<string>? Validate(string userName, string password)
    {
        var found = _users.TryGetValue(userName, out var user);

        var expected = found ? Encoding.UTF8.GetBytes(user!.Password) : _decoyPassword;
        var matches = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), expected);

        if (!found || !matches)
            return null;

        return [.. user!.Roles.Where(Roles.All.Contains)];
    }

    /// <summary>
    /// Configuration mistakes that would silently leave the API open or unusable. Called at startup so the
    /// host fails fast instead of rejecting every caller at runtime.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (_users.Count == 0)
            errors.Add($"No user is configured in '{BasicAuthenticationOptions.SectionName}:Users'.");

        foreach (var user in _users.Values)
        {
            if (string.IsNullOrWhiteSpace(user.Password))
                errors.Add($"User '{user.UserName}' has no password.");

            if (user.Roles.Count == 0)
                errors.Add($"User '{user.UserName}' has no role.");

            foreach (var role in user.Roles.Where(role => !Roles.All.Contains(role)))
                errors.Add($"User '{user.UserName}' has the unknown role '{role}'. Known roles: {string.Join(", ", Roles.All)}.");
        }

        return errors;
    }
}
