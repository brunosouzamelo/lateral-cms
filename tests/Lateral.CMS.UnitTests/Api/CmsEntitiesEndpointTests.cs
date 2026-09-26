using System.Net;
using System.Net.Http.Json;
using Lateral.CMS.UnitTests.Support;
using static Lateral.CMS.UnitTests.Support.CmsApiClientExtensions;

namespace Lateral.CMS.UnitTests.Api;

/// <summary>
/// What each audience sees, and the one write the API allows: the local admin override.
/// </summary>
public class CmsEntitiesEndpointTests(CmsApiFactory factory) : IClassFixture<CmsApiFactory>
{
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_ShowsPublishedEntitiesToConsumers_AndUnpublishedOnesOnlyToAdministrators()
    {
        var published = await PublishAsync();
        var unpublished = await UnPublishAsync();

        var consumerIds = await ListIdsAsync(factory.AsConsumer());
        Assert.Contains(published, consumerIds);
        Assert.DoesNotContain(unpublished, consumerIds);

        var adminIds = await ListIdsAsync(factory.AsAdmin());
        Assert.Contains(published, adminIds);
        Assert.Contains(unpublished, adminIds);
    }

    [Fact]
    public async Task Disable_HidesTheEntityFromConsumers_ButKeepsItForAdministrators()
    {
        var id = await PublishAsync();

        var disabled = await factory.AsAdmin().PutAsJsonAsync($"/api/v1/entities/{id}/disabled", new { isDisabled = true });
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);

        var consumer = factory.AsConsumer();
        Assert.Equal(HttpStatusCode.NotFound, (await consumer.GetAsync($"/api/v1/entities/{id}")).StatusCode);
        Assert.DoesNotContain(id, await ListIdsAsync(consumer));

        var entity = await (await factory.AsAdmin().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();
        Assert.True(entity.GetProperty("isDisabledByAdmin").GetBoolean());
        Assert.NotEqual(default, entity.GetProperty("disabledByAdminDate").GetDateTimeOffset());

        // The override is local: the CMS data behind it is untouched.
        Assert.Equal("Published", entity.GetProperty("status").GetString());
        Assert.Equal(1, entity.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Disable_CanBeUndone()
    {
        var id = await PublishAsync();
        var admin = factory.AsAdmin();

        await admin.PutAsJsonAsync($"/api/v1/entities/{id}/disabled", new { isDisabled = true });
        await admin.PutAsJsonAsync($"/api/v1/entities/{id}/disabled", new { isDisabled = false });

        Assert.Equal(HttpStatusCode.OK, (await factory.AsConsumer().GetAsync($"/api/v1/entities/{id}")).StatusCode);
    }

    [Fact]
    public async Task Disable_SurvivesLaterCmsEvents()
    {
        var id = await PublishAsync();

        await factory.AsAdmin().PutAsJsonAsync($"/api/v1/entities/{id}/disabled", new { isDisabled = true });

        // The CMS keeps publishing; the local override is not part of the data it owns.
        await factory.AsOrganization().PostEventsAsync(PublishEvent(id, 2, Timestamp.AddMinutes(1), new { title = "updated" }));
        await factory.ProcessEventsAsync();

        var entity = await (await factory.AsAdmin().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();

        Assert.Equal(2, entity.GetProperty("version").GetInt32());
        Assert.True(entity.GetProperty("isDisabledByAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await factory.AsConsumer().GetAsync($"/api/v1/entities/{id}")).StatusCode);
    }

    [Fact]
    public async Task Disable_AnswersNotFound_ForAnEntityThatIsNotStored()
    {
        var response = await factory.AsAdmin()
            .PutAsJsonAsync($"/api/v1/entities/{NextEntityId()}/disabled", new { isDisabled = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_AnswersNotFound_ForAnEntityAConsumerMayNotSee()
    {
        var unpublished = await UnPublishAsync();

        // Deliberately the same answer as for an entity that does not exist, so a consumer cannot
        // probe for the identifiers of hidden entities.
        Assert.Equal(HttpStatusCode.NotFound, (await factory.AsConsumer().GetAsync($"/api/v1/entities/{unpublished}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.AsConsumer().GetAsync($"/api/v1/entities/{NextEntityId()}")).StatusCode);
    }

    [Fact]
    public async Task List_FiltersByIdentifierPrefix()
    {
        var id = await PublishAsync();

        var ids = await ListIdsAsync(factory.AsConsumer(), $"?id={id}");

        Assert.Equal([id], ids);
    }

    [Fact]
    public async Task List_IgnoresTheAdminOnlyFilters_ForConsumers()
    {
        var published = await PublishAsync();
        var unpublished = await UnPublishAsync();

        // Asking for unpublished entities does not make them visible.
        var ids = await ListIdsAsync(factory.AsConsumer(), "?status=Unpublished&pageSize=100");

        Assert.DoesNotContain(unpublished, ids);
        Assert.Contains(published, ids);
    }

    [Fact]
    public async Task List_RejectsAPageSizeOverTheLimit()
    {
        var response = await factory.AsConsumer().GetAsync("/api/v1/entities?pageSize=5000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsThePagingEnvelope()
    {
        await PublishAsync();

        var page = await (await factory.AsConsumer().GetAsync("/api/v1/entities?pageIndex=0&pageSize=1")).ReadJsonAsync();

        Assert.Equal(0, page.GetProperty("pageIndex").GetInt32());
        Assert.Single(page.GetProperty("list").EnumerateArray());
        Assert.True(page.GetProperty("total").GetInt32() >= 1);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<string> PublishAsync()
    {
        var id = NextEntityId();

        await factory.AsOrganization().PostEventsAsync(PublishEvent(id, 1, Timestamp));
        await factory.ProcessEventsAsync();

        return id;
    }

    private async Task<string> UnPublishAsync()
    {
        var id = NextEntityId();

        await factory.AsOrganization().PostEventsAsync(UnPublishEvent(id, 1, Timestamp));
        await factory.ProcessEventsAsync();

        return id;
    }

    private static async Task<List<string>> ListIdsAsync(HttpClient client, string query = "?pageSize=100")
    {
        var page = await (await client.GetAsync($"/api/v1/entities{query}")).ReadJsonAsync();

        return [.. page.GetProperty("list").EnumerateArray().Select(entity => entity.GetProperty("id").GetString()!)];
    }
}
