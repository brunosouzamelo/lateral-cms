using Lateral.CMS.Application.Ingestion.CmsEvent.Services;
using Lateral.CMS.Domain.Entities.Content;
using Lateral.CMS.Domain.Enumerations;
using Lateral.CMS.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using CmsEventEntity = Lateral.CMS.Domain.Entities.Ingestion.CmsEvent;

namespace Lateral.CMS.UnitTests.Ingestion;

/// <summary>
/// The event rules, which are the part of the service that has to be right: what each event type does to the
/// stored state, and what happens when events arrive twice, late or out of order.
/// </summary>
public class CmsEventApplierTests : IDisposable
{
    private const string EntityId = "entity-1";

    private readonly CmsTestDatabase _database = new();
    private readonly FakeDateTimeService _clock = new();
    private readonly CmsEventApplier _applier;

    public CmsEventApplierTests() => _applier = new CmsEventApplier(_database.Context, _clock);

    // ---------------------------------------------------------------- publish

    [Fact]
    public async Task Publish_CreatesTheEntity_WhenItIsNotStored()
    {
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));

        Assert.Equal(CmsEventStatus.Applied, outcome.Status);

        var entity = await FindEntityAsync();
        Assert.NotNull(entity);
        Assert.Equal(1, entity.Version);
        Assert.Equal(1, entity.LastPublishedVersion);
        Assert.Equal(CmsEntityStatus.Published, entity.CmsEntityStatusId);
        Assert.Equal(TestCmsEvents.At(0), entity.LastEventTimestamp);
    }

    [Fact]
    public async Task Publish_ReplacesTheData_WhenTheVersionIsNewer()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(1), """{"title":"second"}"""));

        Assert.Equal(CmsEventStatus.Applied, outcome.Status);

        var entity = await FindEntityAsync();
        Assert.Equal(2, entity!.Version);
        Assert.Equal(2, entity.LastPublishedVersion);
        Assert.Equal("""{"title":"second"}""", entity.Payload);
    }

    [Fact]
    public async Task Publish_IsIgnored_WhenTheVersionIsOlderThanTheStoredOne()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 3, TestCmsEvents.At(2), """{"title":"third"}"""));

        // Out-of-order delivery: an older revision arrives after a newer one.
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(1), """{"title":"second"}"""));

        Assert.Equal(CmsEventStatus.Ignored, outcome.Status);

        var entity = await FindEntityAsync();
        Assert.Equal(3, entity!.Version);
        Assert.Equal("""{"title":"third"}""", entity.Payload);
    }

    [Fact]
    public async Task Publish_IsIgnored_WhenTheSameEventIsDeliveredTwice()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));

        Assert.Equal(CmsEventStatus.Ignored, outcome.Status);
        Assert.Equal(1, await CountEntitiesAsync());
    }

    // -------------------------------------------------------------- unPublish

    [Fact]
    public async Task UnPublish_DisablesTheEntity_ButKeepsItsData()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0), """{"title":"kept"}"""));
        var outcome = await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 1, TestCmsEvents.At(1), """{"title":"kept"}"""));

        Assert.Equal(CmsEventStatus.Applied, outcome.Status);

        var entity = await FindEntityAsync();
        Assert.NotNull(entity);
        Assert.Equal(CmsEntityStatus.Unpublished, entity.CmsEntityStatusId);
        Assert.Equal("""{"title":"kept"}""", entity.Payload);
        Assert.Equal(1, entity.LastPublishedVersion);
    }

    /// <summary>
    /// The corner case called out in the brief: version X is published, X+1 is created in the CMS but never
    /// published — so it was never sent — and then X+1 is unpublished. The unPublish event carries the fields
    /// of X+1, so the service must take them: it is the latest version that exists.
    /// </summary>
    [Fact]
    public async Task UnPublish_AdvancesTheStoredVersion_WhenTheNewOneWasNeverPublished()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 4, TestCmsEvents.At(0), """{"title":"v4"}"""));

        var outcome = await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 5, TestCmsEvents.At(1), """{"title":"v5 never published"}"""));

        Assert.Equal(CmsEventStatus.Applied, outcome.Status);

        var entity = await FindEntityAsync();
        Assert.Equal(5, entity!.Version);
        Assert.Equal("""{"title":"v5 never published"}""", entity.Payload);
        Assert.Equal(CmsEntityStatus.Unpublished, entity.CmsEntityStatusId);

        // The last version that ever reached the service through a publish is still 4.
        Assert.Equal(4, entity.LastPublishedVersion);
    }

    [Fact]
    public async Task UnPublish_CreatesTheEntityAlreadyDisabled_WhenNoVersionWasEverStored()
    {
        var outcome = await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 7, TestCmsEvents.At(0), """{"title":"v7"}"""));

        Assert.Equal(CmsEventStatus.Applied, outcome.Status);

        var entity = await FindEntityAsync();
        Assert.NotNull(entity);
        Assert.Equal(7, entity.Version);
        Assert.Null(entity.LastPublishedVersion);
        Assert.Equal(CmsEntityStatus.Unpublished, entity.CmsEntityStatusId);
    }

    [Fact]
    public async Task Publish_RepublishesTheSameVersion_WhenItArrivesAfterTheUnPublish()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 4, TestCmsEvents.At(0)));
        await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 4, TestCmsEvents.At(1)));

        // Same version, later timestamp: the timestamp decides.
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 4, TestCmsEvents.At(2)));

        Assert.Equal(CmsEventStatus.Applied, outcome.Status);
        Assert.Equal(CmsEntityStatus.Published, (await FindEntityAsync())!.CmsEntityStatusId);
    }

    [Fact]
    public async Task UnPublish_Wins_WhenItSharesVersionAndTimestampWithAPublish()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(0)));

        // Ambiguous ordering: the restrictive outcome is kept, so confidential data is not re-exposed
        // by the order in which two events happen to be delivered.
        var unpublish = await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 2, TestCmsEvents.At(0)));
        var republish = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(0)));

        Assert.Equal(CmsEventStatus.Applied, unpublish.Status);
        Assert.Equal(CmsEventStatus.Ignored, republish.Status);
        Assert.Equal(CmsEntityStatus.Unpublished, (await FindEntityAsync())!.CmsEntityStatusId);
    }

    // ----------------------------------------------------------------- delete

    [Fact]
    public async Task Delete_RemovesTheEntity_AndRecordsATombstone()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));

        var outcome = await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(1)));

        Assert.Equal(CmsEventStatus.Applied, outcome.Status);
        Assert.Null(await FindEntityAsync());

        var tombstone = await FindTombstoneAsync();
        Assert.NotNull(tombstone);
        Assert.Equal(TestCmsEvents.At(1), tombstone.DeletedTimestamp);
    }

    [Fact]
    public async Task Delete_RecordsATombstone_WhenTheEntityWasNeverStored()
    {
        var outcome = await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(1)));

        Assert.Equal(CmsEventStatus.Applied, outcome.Status);
        Assert.NotNull(await FindTombstoneAsync());
    }

    [Fact]
    public async Task Delete_IsIgnored_WhenTheEntityIsAlreadyDeleted()
    {
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(1)));
        var outcome = await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(1)));

        Assert.Equal(CmsEventStatus.Ignored, outcome.Status);
    }

    [Fact]
    public async Task Publish_IsIgnored_WhenItPredatesTheDeletion()
    {
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(5)));

        // A publish that was in flight when the delete was processed must not resurrect the entity.
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 9, TestCmsEvents.At(4)));

        Assert.Equal(CmsEventStatus.Ignored, outcome.Status);
        Assert.Null(await FindEntityAsync());
    }

    [Fact]
    public async Task Publish_RecreatesTheEntity_WhenItIsLaterThanTheDeletion()
    {
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(5)));

        // A genuine re-creation in the CMS reuses the identifier; it is later, so it is accepted.
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(6)));

        Assert.Equal(CmsEventStatus.Applied, outcome.Status);
        Assert.NotNull(await FindEntityAsync());
    }

    [Fact]
    public async Task Delete_IsIgnored_ButStillRecordsTheTombstone_WhenTheEntityChangedAfterIt()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(5)));

        // A delete that predates the stored state: dropping the entity would lose a newer change.
        var outcome = await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(3)));

        Assert.Equal(CmsEventStatus.Ignored, outcome.Status);
        Assert.NotNull(await FindEntityAsync());

        // Recorded anyway, so events older than the deletion are still discarded.
        Assert.Equal(TestCmsEvents.At(3), (await FindTombstoneAsync())!.DeletedTimestamp);
    }

    [Fact]
    public async Task Delete_KeepsTheLatestDeletionTimestamp_WhenAnOlderDeleteArrivesAfterwards()
    {
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(5)));
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(2)));

        Assert.Equal(TestCmsEvents.At(5), (await FindTombstoneAsync())!.DeletedTimestamp);
    }

    // ------------------------------------------------------------ consistency

    [Fact]
    public async Task ApplyAsync_Throws_WhenTheEventIsIncomplete()
    {
        var incomplete = TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0));
        incomplete.EventTimestamp = null;

        // Such an event is rejected at the webhook and never reaches the applier; if one ever did, the
        // processor must record a failure instead of writing a half-known state.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _applier.ApplyAsync(incomplete, CancellationToken.None));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<CmsEventOutcome> ApplyAsync(CmsEventEntity cmsEvent)
    {
        var outcome = await _applier.ApplyAsync(cmsEvent, CancellationToken.None);
        await _database.Context.SaveChangesAsync(CancellationToken.None);

        return outcome;
    }

    private async Task<CmsEntity?> FindEntityAsync()
    {
        using var context = _database.CreateContext();
        return await context.CmsEntity.AsNoTracking().FirstOrDefaultAsync(e => e.ExternalId == EntityId);
    }

    private async Task<CmsEntityTombstone?> FindTombstoneAsync()
    {
        using var context = _database.CreateContext();
        return await context.CmsEntityTombstone.AsNoTracking().FirstOrDefaultAsync(t => t.ExternalId == EntityId);
    }

    private async Task<int> CountEntitiesAsync()
    {
        using var context = _database.CreateContext();
        return await context.CmsEntity.CountAsync();
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
