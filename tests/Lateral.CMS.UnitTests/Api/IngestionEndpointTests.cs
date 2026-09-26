using System.Net;
using System.Text;
using System.Text.Json;
using Lateral.CMS.UnitTests.Support;
using static Lateral.CMS.UnitTests.Support.CmsApiClientExtensions;

namespace Lateral.CMS.UnitTests.Api;

/// <summary>
/// The webhook as the CMS sees it: what it accepts, what it reports back, and what the service holds
/// once the batch has been processed.
/// </summary>
public class IngestionEndpointTests(CmsApiFactory factory) : IClassFixture<CmsApiFactory>
{
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Receive_AcknowledgesTheBatch_BeforeProcessingIt()
    {
        var id = NextEntityId();

        var response = await factory.AsOrganization().PostEventsAsync(PublishEvent(id, 1, Timestamp));

        // 202, not 200: the batch is stored and applied afterwards, so the CMS is not kept waiting.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var receipt = await response.ReadJsonAsync();
        Assert.Equal(1, receipt.GetProperty("received").GetInt32());
        Assert.Equal(1, receipt.GetProperty("accepted").GetInt32());
        Assert.Equal(0, receipt.GetProperty("rejected").GetInt32());
        Assert.NotEqual(Guid.Empty, receipt.GetProperty("batchId").GetGuid());
    }

    [Fact]
    public async Task Receive_AcceptsTheValidEvents_AndReportsOnlyTheRejectedOnes()
    {
        var good = NextEntityId();
        var alsoGood = NextEntityId();

        var response = await factory.AsOrganization().PostEventsAsync(
            PublishEvent(good, 1, Timestamp),
            new { type = "publish", id = "no-version", payload = new { title = "x" }, timestamp = Timestamp },
            DeleteEvent(alsoGood, Timestamp));

        var receipt = await response.ReadJsonAsync();

        // One bad event does not cost the CMS the whole delivery.
        Assert.Equal(3, receipt.GetProperty("received").GetInt32());
        Assert.Equal(2, receipt.GetProperty("accepted").GetInt32());

        var rejection = Assert.Single(receipt.GetProperty("rejections").EnumerateArray());
        Assert.Equal(1, rejection.GetProperty("index").GetInt32());
        Assert.Equal("no-version", rejection.GetProperty("id").GetString());
        Assert.Contains(rejection.GetProperty("errors").EnumerateArray(), error => error.GetString()!.Contains("'version'"));
    }

    [Fact]
    public async Task Receive_RejectsAnEmptyBatch()
    {
        var response = await factory.AsOrganization().PostEventsAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Receive_RejectsAPayloadThatIsNotAnObject()
    {
        var response = await factory.AsOrganization().PostEventsAsync(
            new { type = "publish", id = NextEntityId(), version = 1, payload = "not an object", timestamp = Timestamp });

        var receipt = await response.ReadJsonAsync();

        Assert.Equal(0, receipt.GetProperty("accepted").GetInt32());
    }

    [Fact]
    public async Task Receive_RejectsMalformedJson_WithProblemDetails()
    {
        var client = factory.AsOrganization();
        var content = new StringContent("[ { \"type\": ", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/cms/events", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProcessedBatch_MakesTheEntityAvailableToConsumers()
    {
        var id = NextEntityId();

        await factory.AsOrganization().PostEventsAsync(
            PublishEvent(id, 1, Timestamp, new { title = "First", tags = new[] { "a", "b" } }));

        await factory.ProcessEventsAsync();

        var entity = await (await factory.AsConsumer().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();

        Assert.Equal(id, entity.GetProperty("id").GetString());
        Assert.Equal(1, entity.GetProperty("version").GetInt32());
        Assert.Equal("Published", entity.GetProperty("status").GetString());

        // The payload is returned as JSON, not as an escaped string.
        Assert.Equal(JsonValueKind.Object, entity.GetProperty("payload").ValueKind);
        Assert.Equal("First", entity.GetProperty("payload").GetProperty("title").GetString());
    }

    [Fact]
    public async Task ProcessedBatch_AppliesTheEventsInTheOrderTheCmsRecordedThem()
    {
        var id = NextEntityId();

        // Delivered newest first: the stored state must still end up at version 3.
        await factory.AsOrganization().PostEventsAsync(
            PublishEvent(id, 3, Timestamp.AddMinutes(2), new { title = "third" }),
            PublishEvent(id, 1, Timestamp, new { title = "first" }),
            PublishEvent(id, 2, Timestamp.AddMinutes(1), new { title = "second" }));

        await factory.ProcessEventsAsync();

        var entity = await (await factory.AsConsumer().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();

        Assert.Equal(3, entity.GetProperty("version").GetInt32());
        Assert.Equal("third", entity.GetProperty("payload").GetProperty("title").GetString());
    }

    [Fact]
    public async Task ProcessedBatch_ChangesNothing_WhenTheSameBatchIsDeliveredTwice()
    {
        var id = NextEntityId();
        var batch = new[] { PublishEvent(id, 1, Timestamp, new { title = "only" }) };

        await factory.AsOrganization().PostEventsAsync(batch);
        await factory.ProcessEventsAsync();

        // A webhook retry after a timeout: delivery is at-least-once, so applying has to be idempotent.
        await factory.AsOrganization().PostEventsAsync(batch);
        await factory.ProcessEventsAsync();

        var entity = await (await factory.AsConsumer().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();
        Assert.Equal(1, entity.GetProperty("version").GetInt32());

        var events = await (await factory.AsAdmin().GetAsync($"/cms/events?externalId={id}")).ReadJsonAsync();
        var statuses = events.GetProperty("list").EnumerateArray().Select(e => e.GetProperty("status").GetString()).ToList();

        Assert.Equal(2, statuses.Count);
        Assert.Contains("Applied", statuses);
        Assert.Contains("Ignored", statuses);
    }

    [Fact]
    public async Task ProcessedDelete_RemovesTheEntity_WhileUnPublishKeepsIt()
    {
        var deleted = NextEntityId();
        var unpublished = NextEntityId();

        await factory.AsOrganization().PostEventsAsync(
            PublishEvent(deleted, 1, Timestamp),
            PublishEvent(unpublished, 1, Timestamp));

        await factory.ProcessEventsAsync();

        await factory.AsOrganization().PostEventsAsync(
            DeleteEvent(deleted, Timestamp.AddMinutes(1)),
            UnPublishEvent(unpublished, 2, Timestamp.AddMinutes(1), new { title = "still here" }));

        await factory.ProcessEventsAsync();

        var admin = factory.AsAdmin();

        // Hard-deleted: gone even for an administrator.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/entities/{deleted}")).StatusCode);

        // Unpublished: kept in the persistence layer, hidden from consumers.
        var kept = await (await admin.GetAsync($"/api/v1/entities/{unpublished}")).ReadJsonAsync();
        Assert.Equal("Unpublished", kept.GetProperty("status").GetString());
        Assert.Equal("still here", kept.GetProperty("payload").GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await factory.AsConsumer().GetAsync($"/api/v1/entities/{unpublished}")).StatusCode);
    }

    [Fact]
    public async Task EventLog_RecordsEveryEvent_IncludingTheRejectedOnes()
    {
        var id = NextEntityId();

        await factory.AsOrganization().PostEventsAsync(
            PublishEvent(id, 1, Timestamp),
            new { type = "publish", id, version = 0, payload = new { title = "x" }, timestamp = Timestamp });

        await factory.ProcessEventsAsync();

        var events = await (await factory.AsAdmin().GetAsync($"/cms/events?externalId={id}")).ReadJsonAsync();
        var statuses = events.GetProperty("list").EnumerateArray().Select(e => e.GetProperty("status").GetString()).ToList();

        Assert.Contains("Applied", statuses);
        Assert.Contains("Rejected", statuses);
    }

    [Fact]
    public async Task EventLog_NeverReturnsThePayload()
    {
        var id = NextEntityId();

        await factory.AsOrganization().PostEventsAsync(PublishEvent(id, 1, Timestamp, new { secret = "confidential" }));

        var events = await (await factory.AsAdmin().GetAsync($"/cms/events?externalId={id}")).ReadJsonAsync();

        Assert.DoesNotContain("confidential", events.ToString());
    }
}
