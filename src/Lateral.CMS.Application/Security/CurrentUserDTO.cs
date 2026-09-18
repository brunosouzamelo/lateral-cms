using Lateral.CMS.Domain.Constants;

namespace Lateral.CMS.Application.Security;

public class CurrentUserDTO
{
    public string? UserName { get; set; }
    public IReadOnlyCollection<string> Roles { get; set; } = [];

    public bool IsAuthenticated => !string.IsNullOrEmpty(UserName);
    public bool IsAdmin => Roles.Contains(Domain.Constants.Roles.Admin);
}
