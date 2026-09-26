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
[TestFixture]
public class AuthenticationTests
{
    private const string EntitiesRoute = "/api/v1/entities";
    private const string EventsRoute = "/cms/events";

    private CmsApiFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _factory = new CmsApiFactory();

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    // ------------------------------------------------------- authentication

    [Test]
    public async Task Request_WithoutCredentials_IsRejectedWithAChallenge()
    {
        var response = await _factory.CreateClient().GetAsync(EntitiesRoute);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var challenges = response.Headers.WwwAuthenticate.ToList();
        Assert.That(challenges, Has.Count.EqualTo(1));

        Assert.Multiple(() =>
        {
            Assert.That(challenges[0].Scheme, Is.EqualTo("Basic"));
            Assert.That(challenges[0].Parameter, Does.Contain("realm="));
        });
    }

    [TestCase(CmsApiFactory.ConsumerUser, CmsApiFactory.ConsumerPassword)]
    [TestCase(CmsApiFactory.AdminUser, CmsApiFactory.AdminPassword)]
    public async Task Request_WithAValidPair_IsAuthenticated(string userName, string password)
    {
        var response = await _factory.As(userName, password).GetAsync(EntitiesRoute);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // Right user, wrong password.
    [TestCase(CmsApiFactory.ConsumerUser, CmsApiFactory.AdminPassword)]
    [TestCase(CmsApiFactory.ConsumerUser, "")]
    [TestCase(CmsApiFactory.ConsumerUser, "not-a-guid")]
    // Right password, wrong user.
    [TestCase("content-consumer-x", CmsApiFactory.ConsumerPassword)]
    [TestCase("", CmsApiFactory.ConsumerPassword)]
    // The password of one user with the name of another.
    [TestCase(CmsApiFactory.AdminUser, CmsApiFactory.ConsumerPassword)]
    public async Task Request_WithAnInvalidPair_IsRejected(string userName, string password)
    {
        var response = await _factory.As(userName, password).GetAsync(EntitiesRoute);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Request_WithAUserNameDifferingOnlyInCase_IsStillAuthenticated()
    {
        // The user name identifies the caller; only the password is treated as a secret.
        var response = await _factory.As(CmsApiFactory.ConsumerUser.ToUpperInvariant(), CmsApiFactory.ConsumerPassword)
            .GetAsync(EntitiesRoute);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("Basic", "not-base-64!!")]
    [TestCase("Basic", "")]
    [TestCase("Basic", "bm8tY29sb24tYXQtYWxs")]                    // decodes to "no-colon-at-all"
    [TestCase("Bearer", "Y29udGVudC1jb25zdW1lci10OnNlY3JldA==")]   // a well-formed pair under the wrong scheme
    public async Task Request_WithAMalformedAuthorizationHeader_IsRejected(string scheme, string parameter)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, parameter);

        var response = await client.GetAsync(EntitiesRoute);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Request_WithAPasswordContainingAColon_IsParsedCorrectly()
    {
        // RFC 7617: the first colon separates the pair, so a colon inside the password is part of it.
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CmsApiFactory.ConsumerUser}:a:b"));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);

        var response = await client.GetAsync(EntitiesRoute);

        // Not the configured password, so it fails — but as a wrong password, not as a malformed header.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // -------------------------------------------------------- authorization

    [Test]
    public async Task Consumer_CannotPushEvents()
    {
        var response = await _factory.AsConsumer().PostEventsAsync(
            CmsApiClientExtensions.PublishEvent(CmsApiClientExtensions.NextEntityId(), 1, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Organization_CannotReadEntities()
    {
        // The CMS is authenticated, but its account exists only to deliver events.
        var response = await _factory.AsOrganization().GetAsync(EntitiesRoute);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Organization_CanPushEvents()
    {
        var response = await _factory.AsOrganization().PostEventsAsync(
            CmsApiClientExtensions.PublishEvent(CmsApiClientExtensions.NextEntityId(), 1, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    [Test]
    public async Task Consumer_CannotDisableAnEntity()
    {
        var response = await _factory.AsConsumer()
            .PutAsJsonAsync($"{EntitiesRoute}/{CmsApiClientExtensions.NextEntityId()}/disabled", new { isDisabled = true });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Consumer_CannotReadTheEventLog()
    {
        // It lists the identifiers of every entity, including those a consumer may not see.
        var response = await _factory.AsConsumer().GetAsync(EventsRoute);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Administrator_CanReadTheEventLog()
    {
        var response = await _factory.AsAdmin().GetAsync(EventsRoute);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task HealthEndpoints_StayPublic_SoProbesNeedNoCredentials()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
