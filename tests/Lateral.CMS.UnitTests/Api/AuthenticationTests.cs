using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Lateral.CMS.UnitTests.Support;

namespace Lateral.CMS.UnitTests.Api;

/// <summary>
/// Nothing in this service is public, so these tests cover both halves of that claim: who gets in
/// (authentication) and what each caller is then allowed to do (authorization).
/// </summary>
public class AuthenticationTests(CmsApiFactory factory) : IClassFixture<CmsApiFactory>
{
    private const string EntitiesRoute = "/api/v1/entities";
    private const string EventsRoute = "/cms/events";

    // ------------------------------------------------------- authentication

    [Fact]
    public async Task Request_WithoutCredentials_IsRejectedWithAChallenge()
    {
        var response = await factory.CreateClient().GetAsync(EntitiesRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Basic", challenge.Scheme);
        Assert.Contains("realm=", challenge.Parameter);
    }

    [Theory]
    [InlineData(CmsApiFactory.ConsumerUser, CmsApiFactory.ConsumerPassword)]
    [InlineData(CmsApiFactory.AdminUser, CmsApiFactory.AdminPassword)]
    public async Task Request_WithAValidPair_IsAuthenticated(string userName, string password)
    {
        var response = await factory.As(userName, password).GetAsync(EntitiesRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    // Right user, wrong password.
    [InlineData(CmsApiFactory.ConsumerUser, CmsApiFactory.AdminPassword)]
    [InlineData(CmsApiFactory.ConsumerUser, "")]
    [InlineData(CmsApiFactory.ConsumerUser, "not-a-guid")]
    // Right password, wrong user.
    [InlineData("content-consumer-x", CmsApiFactory.ConsumerPassword)]
    [InlineData("", CmsApiFactory.ConsumerPassword)]
    // The password of one user with the name of another.
    [InlineData(CmsApiFactory.AdminUser, CmsApiFactory.ConsumerPassword)]
    public async Task Request_WithAnInvalidPair_IsRejected(string userName, string password)
    {
        var response = await factory.As(userName, password).GetAsync(EntitiesRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithAUserNameDifferingOnlyInCase_IsStillAuthenticated()
    {
        // The user name identifies the caller; only the password is treated as a secret.
        var response = await factory.As(CmsApiFactory.ConsumerUser.ToUpperInvariant(), CmsApiFactory.ConsumerPassword)
            .GetAsync(EntitiesRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Basic", "not-base-64!!")]
    [InlineData("Basic", "")]
    [InlineData("Basic", "bm8tY29sb24tYXQtYWxs")]                    // decodes to "no-colon-at-all"
    [InlineData("Bearer", "Y29udGVudC1jb25zdW1lci10OnNlY3JldA==")]   // a well-formed pair under the wrong scheme
    public async Task Request_WithAMalformedAuthorizationHeader_IsRejected(string scheme, string parameter)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, parameter);

        var response = await client.GetAsync(EntitiesRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithAPasswordContainingAColon_IsParsedCorrectly()
    {
        // RFC 7617: the first colon separates the pair, so a colon inside the password is part of it.
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CmsApiFactory.ConsumerUser}:a:b"));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);

        // Not the configured password, so it fails — but as a wrong password, not as a malformed header.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(EntitiesRoute)).StatusCode);
    }

    // -------------------------------------------------------- authorization

    [Fact]
    public async Task Consumer_CannotPushEvents()
    {
        var response = await factory.AsConsumer().PostEventsAsync(
            CmsApiClientExtensions.PublishEvent(CmsApiClientExtensions.NextEntityId(), 1, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Organization_CannotReadEntities()
    {
        // The CMS is authenticated, but its account exists only to deliver events.
        var response = await factory.AsOrganization().GetAsync(EntitiesRoute);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Organization_CanPushEvents()
    {
        var response = await factory.AsOrganization().PostEventsAsync(
            CmsApiClientExtensions.PublishEvent(CmsApiClientExtensions.NextEntityId(), 1, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Consumer_CannotDisableAnEntity()
    {
        var response = await factory.AsConsumer()
            .PutAsJsonAsync($"{EntitiesRoute}/{CmsApiClientExtensions.NextEntityId()}/disabled", new { isDisabled = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Consumer_CannotReadTheEventLog()
    {
        // It lists the identifiers of every entity, including those a consumer may not see.
        var response = await factory.AsConsumer().GetAsync(EventsRoute);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_CanReadTheEventLog()
    {
        var response = await factory.AsAdmin().GetAsync(EventsRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoints_StayPublic_SoProbesNeedNoCredentials()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
}
