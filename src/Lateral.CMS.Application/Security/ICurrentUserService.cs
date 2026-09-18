namespace Lateral.CMS.Application.Security;

public interface ICurrentUserService
{
    CurrentUserDTO CurrentUser { get; }
}
