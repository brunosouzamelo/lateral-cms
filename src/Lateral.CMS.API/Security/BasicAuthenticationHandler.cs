using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Lateral.CMS.Application.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Lateral.CMS.API.Security;

/// <summary>
/// RFC 7617 Basic authentication against the users configured in <c>Authentication:Basic</c>.
/// The scheme is the one the CMS already uses, so the webhook needs no extra client support; the callers,
/// however, are separate users (see <see cref="Domain.Constants.Roles"/>).
/// </summary>
public class BasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    BasicAuthenticationCredentialStore credentialStore)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const int UserNameMaxLength = 50;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var header))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!AuthenticationHeaderValue.TryParse(header.ToString(), out var userName, out var password))
        {
            Logger.LogWarning("Rejected request to {Path}: the Authorization header is not a valid Basic credential.", Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("Invalid Basic credentials."));
        }

        var roles = credentialStore.Validate(userName, password);

        if (roles is null)
        {
            Logger.LogWarning("Rejected request to {Path}: unknown user or wrong password for '{UserName}'.",
                Request.Path, TextSanitizer.ForStorage(userName, UserNameMaxLength));

            return Task.FromResult(AuthenticateResult.Fail("Invalid user name or password."));
        }

        var identity = new ClaimsIdentity(BasicAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.Name, userName));
        identity.AddClaims(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate =
            $"{BasicAuthenticationDefaults.AuthenticationScheme} realm=\"{credentialStore.Realm}\", charset=\"UTF-8\"";

        return base.HandleChallengeAsync(properties);
    }

    private static class AuthenticationHeaderValue
    {
        public static bool TryParse(string? value, out string userName, out string password)
        {
            userName = string.Empty;
            password = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var separator = value.IndexOf(' ');

            if (separator <= 0
                || !value.AsSpan(0, separator).Equals(BasicAuthenticationDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase))
                return false;

            var encoded = value[(separator + 1)..].Trim();

            if (encoded.Length == 0)
                return false;

            string decoded;

            try
            {
                // RFC 7617: the credentials are UTF-8 text; invalid bytes must not be silently replaced.
                decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Convert.FromBase64String(encoded));
            }
            catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
            {
                return false;
            }

            var colon = decoded.IndexOf(':');

            // The user name cannot contain a colon; the password can.
            if (colon <= 0)
                return false;

            userName = decoded[..colon];
            password = decoded[(colon + 1)..];

            return true;
        }
    }
}
