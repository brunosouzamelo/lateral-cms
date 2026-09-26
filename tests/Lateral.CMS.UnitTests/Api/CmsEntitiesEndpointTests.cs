using System.Net;
using System.Net.Http.Json;
using Lateral.CMS.UnitTests.Support;
using static Lateral.CMS.UnitTests.Support.CmsApiClientExtensions;

namespace Lateral.CMS.UnitTests.Api;

/// <summary>
/// What each audience sees, and the one write the API allows: the local admin override.
/// </summary>
[TestFixture]
public class CmsEntitiesEndpointTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private CmsApiFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _factory = new CmsApiFactory();

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [Test]
    public async Task List_ShowsPublishedEntitiesToConsumers_AndUnpublishedOnesOnlyToAdministrators()
    {
        var published = await PublishAsync();
        var unpublished = await UnPublishAsync();

        var consumerIds = await ListIdsAsync(_factory.AsConsumer());
        var adminIds = await ListIdsAsync(_factory.AsAdmin());

        Assert.Multiple(() =>
        {
            Assert.That(consumerIds, Does.Contain(published));
            Assert.That(consumerIds, Does.Not.Contain(unpublished));
            Assert.That(adminIds, Does.Contain(published));
            Assert.That(adminIds, Does.Contain(unpublished));
        });
    }

    [Test]
    public async Task Disable_HidesTheEntityFromConsumers_ButKeepsItForAdministrators()
    {
        var id = await PublishAsync();

        var disabled = await _factory.AsAdmin().PutAsJsonAsync($"/api/v1/entities/{id}/disabled", new { isDisabled = true });
        Assert.That(disabled.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var consumer = _factory.AsConsumer();
        var consumerResponse = await consumer.GetAsync($"/api/v1/entities/{id}");
        var consumerIds = await ListIdsAsync(consumer);

        Assert.Multiple(() =>
        {
            Assert.That(consumerResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(consumerIds, Does.Not.Contain(id));
        });

        var entity = await (await _factory.AsAdmin().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entity.GetProperty("isDisabledByAdmin").GetBoolean(), Is.True);
            Assert.That(entity.GetProperty("disabledByAdminDate").GetDateTimeOffset(), Is.Not.EqualTo(default(DateTimeOffset)));

            // The override is local: the CMS data behind it is untouched.
            Assert.That(entity.GetProperty("status").GetString(), Is.EqualTo("Published"));
            Assert.That(entity.GetProperty("version").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Disable_CanBeUndone()
    {
        var id = await PublishAsync();
        var admin = _factory.AsAdmin();

        await admin.PutAsJsonAsync($"/api/v1/entities/{id}/disabled", new { isDisabled = true });
        await admin.PutAsJsonAsync($"/api/v1/entities/{id}/disabled", new { isDisabled = false });

        var response = await _factory.AsConsumer().GetAsync($"/api/v1/entities/{id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Disable_SurvivesLaterCmsEvents()
    {
        var id = await PublishAsync();

        await _factory.AsAdmin().PutAsJsonAsync($"/api/v1/entities/{id}/disabled", new { isDisabled = true });

        // The CMS keeps publishing; the local override is not part of the data it owns.
        await _factory.AsOrganization().PostEventsAsync(PublishEvent(id, 2, Timestamp.AddMinutes(1), new { title = "updated" }));
        await _factory.ProcessEventsAsync();

        var entity = await (await _factory.AsAdmin().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();
        var consumerResponse = await _factory.AsConsumer().GetAsync($"/api/v1/entities/{id}");

        Assert.Multiple(() =>
        {
            Assert.That(entity.GetProperty("version").GetInt32(), Is.EqualTo(2));
            Assert.That(entity.GetProperty("isDisabledByAdmin").GetBoolean(), Is.True);
            Assert.That(consumerResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Disable_AnswersNotFound_ForAnEntityThatIsNotStored()
    {
        var response = await _factory.AsAdmin()
            .PutAsJsonAsync($"/api/v1/entities/{NextEntityId()}/disabled", new { isDisabled = true });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Get_AnswersNotFound_ForAnEntityAConsumerMayNotSee()
    {
        var unpublished = await UnPublishAsync();
        var consumer = _factory.AsConsumer();

        var hidden = await consumer.GetAsync($"/api/v1/entities/{unpublished}");
        var missing = await consumer.GetAsync($"/api/v1/entities/{NextEntityId()}");

        // Deliberately the same answer as for an entity that does not exist, so a consumer cannot
        // probe for the identifiers of hidden entities.
        Assert.Multiple(() =>
        {
            Assert.That(hidden.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task List_FiltersByIdentifierPrefix()
    {
        var id = await PublishAsync();

        var ids = await ListIdsAsync(_factory.AsConsumer(), $"?id={id}");

        Assert.That(ids, Is.EqualTo(new[] { id }));
    }

    [Test]
    public async Task List_IgnoresTheAdminOnlyFilters_ForConsumers()
    {
        var published = await PublishAsync();
        var unpublished = await UnPublishAsync();

        // Asking for unpublished entities does not make them visible.
        var ids = await ListIdsAsync(_factory.AsConsumer(), "?status=Unpublished&pageSize=100");

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Not.Contain(unpublished));
            Assert.That(ids, Does.Contain(published));
        });
    }

    [Test]
    public async Task List_RejectsAPageSizeOverTheLimit()
    {
        var response = await _factory.AsConsumer().GetAsync("/api/v1/entities?pageSize=5000");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task List_ReturnsThePagingEnvelope()
    {
        await PublishAsync();

        var page = await (await _factory.AsConsumer().GetAsync("/api/v1/entities?pageIndex=0&pageSize=1")).ReadJsonAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.GetProperty("pageIndex").GetInt32(), Is.EqualTo(0));
            Assert.That(page.GetProperty("list").EnumerateArray().ToList(), Has.Count.EqualTo(1));
            Assert.That(page.GetProperty("total").GetInt32(), Is.GreaterThanOrEqualTo(1));
        });
    }

    // ---------------------------------------------------------------- helpers

    private async Task<string> PublishAsync()
    {
        var id = NextEntityId();

        await _factory.AsOrganization().PostEventsAsync(PublishEvent(id, 1, Timestamp));
        await _factory.ProcessEventsAsync();

        return id;
    }

    private async Task<string> UnPublishAsync()
    {
        var id = NextEntityId();

        await _factory.AsOrganization().PostEventsAsync(UnPublishEvent(id, 1, Timestamp));
        await _factory.ProcessEventsAsync();

        return id;
    }

    private static async Task<List<string>> ListIdsAsync(HttpClient client, string query = "?pageSize=100")
    {
        var page = await (await client.GetAsync($"/api/v1/entities{query}")).ReadJsonAsync();

        return [.. page.GetProperty("list").EnumerateArray().Select(entity => entity.GetProperty("id").GetString()!)];
    }
}
