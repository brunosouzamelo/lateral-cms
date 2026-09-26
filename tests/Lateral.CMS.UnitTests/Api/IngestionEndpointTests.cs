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
[TestFixture]
public class IngestionEndpointTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private CmsApiFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _factory = new CmsApiFactory();

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [Test]
    public async Task Receive_AcknowledgesTheBatch_BeforeProcessingIt()
    {
        var id = NextEntityId();

        var response = await _factory.AsOrganization().PostEventsAsync(PublishEvent(id, 1, Timestamp));

        // 202, not 200: the batch is stored and applied afterwards, so the CMS is not kept waiting.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var receipt = await response.ReadJsonAsync();

        Assert.Multiple(() =>
        {
            Assert.That(receipt.GetProperty("received").GetInt32(), Is.EqualTo(1));
            Assert.That(receipt.GetProperty("accepted").GetInt32(), Is.EqualTo(1));
            Assert.That(receipt.GetProperty("rejected").GetInt32(), Is.EqualTo(0));
            Assert.That(receipt.GetProperty("batchId").GetGuid(), Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public async Task Receive_AcceptsTheValidEvents_AndReportsOnlyTheRejectedOnes()
    {
        var good = NextEntityId();
        var alsoGood = NextEntityId();

        var response = await _factory.AsOrganization().PostEventsAsync(
            PublishEvent(good, 1, Timestamp),
            new { type = "publish", id = "no-version", payload = new { title = "x" }, timestamp = Timestamp },
            DeleteEvent(alsoGood, Timestamp));

        var receipt = await response.ReadJsonAsync();

        // One bad event does not cost the CMS the whole delivery.
        Assert.Multiple(() =>
        {
            Assert.That(receipt.GetProperty("received").GetInt32(), Is.EqualTo(3));
            Assert.That(receipt.GetProperty("accepted").GetInt32(), Is.EqualTo(2));
        });

        var rejections = receipt.GetProperty("rejections").EnumerateArray().ToList();
        Assert.That(rejections, Has.Count.EqualTo(1));

        var errors = string.Join(" | ", rejections[0].GetProperty("errors").EnumerateArray().Select(e => e.GetString()));

        Assert.Multiple(() =>
        {
            Assert.That(rejections[0].GetProperty("index").GetInt32(), Is.EqualTo(1));
            Assert.That(rejections[0].GetProperty("id").GetString(), Is.EqualTo("no-version"));
            Assert.That(errors, Does.Contain("'version'"));
        });
    }

    [Test]
    public async Task Receive_RejectsAnEmptyBatch()
    {
        var response = await _factory.AsOrganization().PostEventsAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Receive_RejectsAPayloadThatIsNotAnObject()
    {
        var response = await _factory.AsOrganization().PostEventsAsync(
            new { type = "publish", id = NextEntityId(), version = 1, payload = "not an object", timestamp = Timestamp });

        var receipt = await response.ReadJsonAsync();

        Assert.That(receipt.GetProperty("accepted").GetInt32(), Is.EqualTo(0));
    }

    [Test]
    public async Task Receive_RejectsMalformedJson_WithProblemDetails()
    {
        var client = _factory.AsOrganization();
        var content = new StringContent("[ { \"type\": ", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/cms/events", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ProcessedBatch_MakesTheEntityAvailableToConsumers()
    {
        var id = NextEntityId();

        await _factory.AsOrganization().PostEventsAsync(
            PublishEvent(id, 1, Timestamp, new { title = "First", tags = new[] { "a", "b" } }));

        await _factory.ProcessEventsAsync();

        var entity = await (await _factory.AsConsumer().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entity.GetProperty("id").GetString(), Is.EqualTo(id));
            Assert.That(entity.GetProperty("version").GetInt32(), Is.EqualTo(1));
            Assert.That(entity.GetProperty("status").GetString(), Is.EqualTo("Published"));

            // The payload is returned as JSON, not as an escaped string.
            Assert.That(entity.GetProperty("payload").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(entity.GetProperty("payload").GetProperty("title").GetString(), Is.EqualTo("First"));
        });
    }

    [Test]
    public async Task ProcessedBatch_AppliesTheEventsInTheOrderTheCmsRecordedThem()
    {
        var id = NextEntityId();

        // Delivered newest first: the stored state must still end up at version 3.
        await _factory.AsOrganization().PostEventsAsync(
            PublishEvent(id, 3, Timestamp.AddMinutes(2), new { title = "third" }),
            PublishEvent(id, 1, Timestamp, new { title = "first" }),
            PublishEvent(id, 2, Timestamp.AddMinutes(1), new { title = "second" }));

        await _factory.ProcessEventsAsync();

        var entity = await (await _factory.AsConsumer().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entity.GetProperty("version").GetInt32(), Is.EqualTo(3));
            Assert.That(entity.GetProperty("payload").GetProperty("title").GetString(), Is.EqualTo("third"));
        });
    }

    [Test]
    public async Task ProcessedBatch_ChangesNothing_WhenTheSameBatchIsDeliveredTwice()
    {
        var id = NextEntityId();
        var batch = new[] { PublishEvent(id, 1, Timestamp, new { title = "only" }) };

        await _factory.AsOrganization().PostEventsAsync(batch);
        await _factory.ProcessEventsAsync();

        // A webhook retry after a timeout: delivery is at-least-once, so applying has to be idempotent.
        await _factory.AsOrganization().PostEventsAsync(batch);
        await _factory.ProcessEventsAsync();

        var entity = await (await _factory.AsConsumer().GetAsync($"/api/v1/entities/{id}")).ReadJsonAsync();
        Assert.That(entity.GetProperty("version").GetInt32(), Is.EqualTo(1));

        var events = await (await _factory.AsAdmin().GetAsync($"/cms/events?externalId={id}")).ReadJsonAsync();
        var statuses = events.GetProperty("list").EnumerateArray().Select(e => e.GetProperty("status").GetString()).ToList();

        Assert.That(statuses, Has.Count.EqualTo(2));

        Assert.Multiple(() =>
        {
            Assert.That(statuses, Does.Contain("Applied"));
            Assert.That(statuses, Does.Contain("Ignored"));
        });
    }

    [Test]
    public async Task ProcessedDelete_RemovesTheEntity_WhileUnPublishKeepsIt()
    {
        var deleted = NextEntityId();
        var unpublished = NextEntityId();

        await _factory.AsOrganization().PostEventsAsync(
            PublishEvent(deleted, 1, Timestamp),
            PublishEvent(unpublished, 1, Timestamp));

        await _factory.ProcessEventsAsync();

        await _factory.AsOrganization().PostEventsAsync(
            DeleteEvent(deleted, Timestamp.AddMinutes(1)),
            UnPublishEvent(unpublished, 2, Timestamp.AddMinutes(1), new { title = "still here" }));

        await _factory.ProcessEventsAsync();

        var admin = _factory.AsAdmin();

        // Hard-deleted: gone even for an administrator.
        var deletedResponse = await admin.GetAsync($"/api/v1/entities/{deleted}");
        Assert.That(deletedResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // Unpublished: kept in the persistence layer, hidden from consumers.
        var kept = await (await admin.GetAsync($"/api/v1/entities/{unpublished}")).ReadJsonAsync();
        var consumerResponse = await _factory.AsConsumer().GetAsync($"/api/v1/entities/{unpublished}");

        Assert.Multiple(() =>
        {
            Assert.That(kept.GetProperty("status").GetString(), Is.EqualTo("Unpublished"));
            Assert.That(kept.GetProperty("payload").GetProperty("title").GetString(), Is.EqualTo("still here"));
            Assert.That(consumerResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task EventLog_RecordsEveryEvent_IncludingTheRejectedOnes()
    {
        var id = NextEntityId();

        await _factory.AsOrganization().PostEventsAsync(
            PublishEvent(id, 1, Timestamp),
            new { type = "publish", id, version = 0, payload = new { title = "x" }, timestamp = Timestamp });

        await _factory.ProcessEventsAsync();

        var events = await (await _factory.AsAdmin().GetAsync($"/cms/events?externalId={id}")).ReadJsonAsync();
        var statuses = events.GetProperty("list").EnumerateArray().Select(e => e.GetProperty("status").GetString()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(statuses, Does.Contain("Applied"));
            Assert.That(statuses, Does.Contain("Rejected"));
        });
    }

    [Test]
    public async Task EventLog_CarriesTheCorrelationIdentifierTheCmsSent()
    {
        var id = NextEntityId();
        var correlationId = $"cms-trace-{Guid.NewGuid():N}";

        var client = _factory.AsOrganization();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var response = await client.PostEventsAsync(PublishEvent(id, 1, Timestamp));

        // Echoed back, so the CMS can tie its own log line to this delivery.
        Assert.That(response.Headers.GetValues("X-Correlation-ID"), Does.Contain(correlationId));

        // And stored, so the delivery can be found later without going through the logs at all.
        var events = await (await _factory.AsAdmin().GetAsync($"/cms/events?correlationId={correlationId}")).ReadJsonAsync();
        var rows = events.GetProperty("list").EnumerateArray().ToList();

        Assert.That(rows, Has.Count.EqualTo(1));

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].GetProperty("correlationId").GetString(), Is.EqualTo(correlationId));
            Assert.That(rows[0].GetProperty("externalId").GetString(), Is.EqualTo(id));
        });
    }

    [Test]
    public async Task EventLog_StillCorrelates_WhenTheCmsSendsNoIdentifier()
    {
        var id = NextEntityId();

        await _factory.AsOrganization().PostEventsAsync(PublishEvent(id, 1, Timestamp));

        var events = await (await _factory.AsAdmin().GetAsync($"/cms/events?externalId={id}")).ReadJsonAsync();
        var row = events.GetProperty("list").EnumerateArray().Single();

        // Falls back to the trace identifier of the request, so no row is left uncorrelated.
        Assert.That(row.GetProperty("correlationId").GetString(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task EventLog_NeverReturnsThePayload()
    {
        var id = NextEntityId();

        await _factory.AsOrganization().PostEventsAsync(PublishEvent(id, 1, Timestamp, new { secret = "confidential" }));

        var events = await (await _factory.AsAdmin().GetAsync($"/cms/events?externalId={id}")).ReadJsonAsync();

        Assert.That(events.ToString(), Does.Not.Contain("confidential"));
    }
}
