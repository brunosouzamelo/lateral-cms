using System.Security.Claims;
using Lateral.CMS.Application.Security;

namespace Lateral.CMS.API.Security;

/// <summary>
/// Exposes the authenticated caller to the application layer, which decides what the caller may see
/// without depending on ASP.NET Core.
/// </summary>
public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public CurrentUserDTO CurrentUser
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true)
                return new CurrentUserDTO();

            return new CurrentUserDTO
            {
                UserName = principal.Identity.Name,
                Roles = [.. principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)]
            };
        }
    }
}
