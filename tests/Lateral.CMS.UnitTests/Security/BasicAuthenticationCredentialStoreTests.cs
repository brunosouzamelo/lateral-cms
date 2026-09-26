using Lateral.CMS.API.Configuration;
using Lateral.CMS.API.Security;
using Lateral.CMS.Domain.Constants;
using Microsoft.Extensions.Options;

namespace Lateral.CMS.UnitTests.Security;

/// <summary>Which user-password combinations are accepted, and what the accepted ones are allowed to be.</summary>
public class BasicAuthenticationCredentialStoreTests
{
    private const string OrganizationUser = "cms-webhook-client";
    private const string OrganizationPassword = "0f1a5f6e-6a23-4f5f-9b7b-6f3f0d9d0a11";
    private const string AdminUser = "content-admin-root";
    private const string AdminPassword = "3c4f9a11-2d3e-4a5b-8c9d-0e1f2a3b4c5d";

    [Fact]
    public void Validate_Accepts_AKnownUserWithTheRightPassword()
    {
        var roles = Store().Validate(OrganizationUser, OrganizationPassword);

        Assert.NotNull(roles);
        Assert.Equal([Roles.Organization], roles);
    }

    [Fact]
    public void Validate_Rejects_AKnownUserWithTheWrongPassword()
        => Assert.Null(Store().Validate(OrganizationUser, AdminPassword));

    [Fact]
    public void Validate_Rejects_AnUnknownUser()
        => Assert.Null(Store().Validate("someone-else", OrganizationPassword));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0f1a5f6e-6a23-4f5f-9b7b-6f3f0d9d0a1")]  // one character short
    [InlineData("0F1A5F6E-6A23-4F5F-9B7B-6F3F0D9D0A11")] // same value, different case
    public void Validate_Rejects_APasswordThatIsNotExactlyTheConfiguredOne(string password)
        => Assert.Null(Store().Validate(OrganizationUser, password));

    [Fact]
    public void Validate_KeepsTheRolesOfEachUserSeparate()
    {
        var store = Store();

        // The CMS pushes events and reads nothing; the administrator reads and overrides but pushes nothing.
        Assert.Equal([Roles.Organization], store.Validate(OrganizationUser, OrganizationPassword));
        Assert.Equal([Roles.User, Roles.Admin], store.Validate(AdminUser, AdminPassword));
    }

    [Fact]
    public void Validate_DropsRolesThatAreNotKnown_SoATypoCannotGrantAccess()
    {
        var store = Store(new BasicAuthenticationUserOptions
        {
            UserName = "typo-user-name-1",
            Password = AdminPassword,
            Roles = ["Administrator"]
        });

        Assert.Empty(store.Validate("typo-user-name-1", AdminPassword)!);
    }

    [Fact]
    public void Validate_ReportsAUserWithoutAPassword()
    {
        var errors = Store(new BasicAuthenticationUserOptions { UserName = "no-password-user", Roles = [Roles.User] }).Validate();

        Assert.Contains(errors, error => error.Contains("no password"));
    }

    [Fact]
    public void Validate_ReportsAUserWithAnUnknownRole()
    {
        var errors = Store(new BasicAuthenticationUserOptions
        {
            UserName = "typo-user-name-1",
            Password = AdminPassword,
            Roles = ["Administrator"]
        }).Validate();

        Assert.Contains(errors, error => error.Contains("unknown role"));
    }

    [Fact]
    public void Validate_ReportsAnEmptyConfiguration_SoTheApiDoesNotStartLockedOut()
    {
        var store = new BasicAuthenticationCredentialStore(Options.Create(new BasicAuthenticationOptions()));

        Assert.Contains(store.Validate(), error => error.Contains("No user is configured"));
    }

    [Fact]
    public void Validate_AcceptsTheConfigurationUsedByTheApi()
        => Assert.Empty(Store().Validate());

    private static BasicAuthenticationCredentialStore Store(params BasicAuthenticationUserOptions[] extraUsers)
    {
        var options = new BasicAuthenticationOptions
        {
            Users =
            [
                new() { UserName = OrganizationUser, Password = OrganizationPassword, Roles = [Roles.Organization] },
                new() { UserName = "content-consumer-1", Password = "7b2c1d3e-4f5a-6b7c-8d9e-0f1a2b3c4d5e", Roles = [Roles.User] },
                new() { UserName = AdminUser, Password = AdminPassword, Roles = [Roles.User, Roles.Admin] },
                .. extraUsers
            ]
        };

        return new BasicAuthenticationCredentialStore(Options.Create(options));
    }
}
