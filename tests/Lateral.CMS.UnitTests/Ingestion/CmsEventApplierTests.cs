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
[TestFixture]
public class CmsEventApplierTests
{
    private const string EntityId = "entity-1";

    private CmsTestDatabase _database = null!;
    private CmsEventApplier _applier = null!;

    [SetUp]
    public void SetUp()
    {
        // A database of its own per test: NUnit reuses the fixture instance, so nothing may carry over.
        _database = new CmsTestDatabase();
        _applier = new CmsEventApplier(_database.Context, new FakeDateTimeService());
    }

    [TearDown]
    public void TearDown() => _database.Dispose();

    // ---------------------------------------------------------------- publish

    [Test]
    public async Task Publish_CreatesTheEntity_WhenItIsNotStored()
    {
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));
        var entity = await FindEntityAsync();

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Applied));
        Assert.That(entity, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(entity!.Version, Is.EqualTo(1));
            Assert.That(entity.LastPublishedVersion, Is.EqualTo(1));
            Assert.That(entity.CmsEntityStatusId, Is.EqualTo(CmsEntityStatus.Published));
            Assert.That(entity.LastEventTimestamp, Is.EqualTo(TestCmsEvents.At(0)));
        });
    }

    [Test]
    public async Task Publish_ReplacesTheData_WhenTheVersionIsNewer()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));

        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(1), """{"title":"second"}"""));
        var entity = await FindEntityAsync();

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Applied));

        Assert.Multiple(() =>
        {
            Assert.That(entity!.Version, Is.EqualTo(2));
            Assert.That(entity.LastPublishedVersion, Is.EqualTo(2));
            Assert.That(entity.Payload, Is.EqualTo("""{"title":"second"}"""));
        });
    }

    [Test]
    public async Task Publish_IsIgnored_WhenTheVersionIsOlderThanTheStoredOne()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 3, TestCmsEvents.At(2), """{"title":"third"}"""));

        // Out-of-order delivery: an older revision arrives after a newer one.
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(1), """{"title":"second"}"""));
        var entity = await FindEntityAsync();

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Ignored));

        Assert.Multiple(() =>
        {
            Assert.That(entity!.Version, Is.EqualTo(3));
            Assert.That(entity.Payload, Is.EqualTo("""{"title":"third"}"""));
        });
    }

    [Test]
    public async Task Publish_IsIgnored_WhenTheSameEventIsDeliveredTwice()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));

        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Ignored));
        Assert.That(await CountEntitiesAsync(), Is.EqualTo(1));
    }

    // -------------------------------------------------------------- unPublish

    [Test]
    public async Task UnPublish_DisablesTheEntity_ButKeepsItsData()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0), """{"title":"kept"}"""));

        var outcome = await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 1, TestCmsEvents.At(1), """{"title":"kept"}"""));
        var entity = await FindEntityAsync();

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Applied));
        Assert.That(entity, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(entity!.CmsEntityStatusId, Is.EqualTo(CmsEntityStatus.Unpublished));
            Assert.That(entity.Payload, Is.EqualTo("""{"title":"kept"}"""));
            Assert.That(entity.LastPublishedVersion, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// The corner case called out in the brief: version X is published, X+1 is created in the CMS but never
    /// published — so it was never sent — and then X+1 is unpublished. The unPublish event carries the fields
    /// of X+1, so the service must take them: it is the latest version that exists.
    /// </summary>
    [Test]
    public async Task UnPublish_AdvancesTheStoredVersion_WhenTheNewOneWasNeverPublished()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 4, TestCmsEvents.At(0), """{"title":"v4"}"""));

        var outcome = await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 5, TestCmsEvents.At(1), """{"title":"v5 never published"}"""));
        var entity = await FindEntityAsync();

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Applied));

        Assert.Multiple(() =>
        {
            Assert.That(entity!.Version, Is.EqualTo(5));
            Assert.That(entity.Payload, Is.EqualTo("""{"title":"v5 never published"}"""));
            Assert.That(entity.CmsEntityStatusId, Is.EqualTo(CmsEntityStatus.Unpublished));

            // The last version that ever reached the service through a publish is still 4.
            Assert.That(entity.LastPublishedVersion, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task UnPublish_CreatesTheEntityAlreadyDisabled_WhenNoVersionWasEverStored()
    {
        var outcome = await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 7, TestCmsEvents.At(0), """{"title":"v7"}"""));
        var entity = await FindEntityAsync();

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Applied));
        Assert.That(entity, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(entity!.Version, Is.EqualTo(7));
            Assert.That(entity.LastPublishedVersion, Is.Null);
            Assert.That(entity.CmsEntityStatusId, Is.EqualTo(CmsEntityStatus.Unpublished));
        });
    }

    [Test]
    public async Task Publish_RepublishesTheSameVersion_WhenItArrivesAfterTheUnPublish()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 4, TestCmsEvents.At(0)));
        await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 4, TestCmsEvents.At(1)));

        // Same version, later timestamp: the timestamp decides.
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 4, TestCmsEvents.At(2)));
        var entity = await FindEntityAsync();

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Applied));
        Assert.That(entity!.CmsEntityStatusId, Is.EqualTo(CmsEntityStatus.Published));
    }

    [Test]
    public async Task UnPublish_Wins_WhenItSharesVersionAndTimestampWithAPublish()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(0)));

        // Ambiguous ordering: the restrictive outcome is kept, so confidential data is not re-exposed
        // by the order in which two events happen to be delivered.
        var unpublish = await ApplyAsync(TestCmsEvents.UnPublish(EntityId, version: 2, TestCmsEvents.At(0)));
        var republish = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(0)));
        var entity = await FindEntityAsync();

        Assert.Multiple(() =>
        {
            Assert.That(unpublish.Status, Is.EqualTo(CmsEventStatus.Applied));
            Assert.That(republish.Status, Is.EqualTo(CmsEventStatus.Ignored));
            Assert.That(entity!.CmsEntityStatusId, Is.EqualTo(CmsEntityStatus.Unpublished));
        });
    }

    // ----------------------------------------------------------------- delete

    [Test]
    public async Task Delete_RemovesTheEntity_AndRecordsATombstone()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0)));

        var outcome = await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(1)));
        var tombstone = await FindTombstoneAsync();

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Applied));
        Assert.That(await FindEntityAsync(), Is.Null);
        Assert.That(tombstone, Is.Not.Null);
        Assert.That(tombstone!.DeletedTimestamp, Is.EqualTo(TestCmsEvents.At(1)));
    }

    [Test]
    public async Task Delete_RecordsATombstone_WhenTheEntityWasNeverStored()
    {
        var outcome = await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(1)));

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Applied));
        Assert.That(await FindTombstoneAsync(), Is.Not.Null);
    }

    [Test]
    public async Task Delete_IsIgnored_WhenTheEntityIsAlreadyDeleted()
    {
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(1)));

        var outcome = await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(1)));

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Ignored));
    }

    [Test]
    public async Task Publish_IsIgnored_WhenItPredatesTheDeletion()
    {
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(5)));

        // A publish that was in flight when the delete was processed must not resurrect the entity.
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 9, TestCmsEvents.At(4)));

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Ignored));
        Assert.That(await FindEntityAsync(), Is.Null);
    }

    [Test]
    public async Task Publish_RecreatesTheEntity_WhenItIsLaterThanTheDeletion()
    {
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(5)));

        // A genuine re-creation in the CMS reuses the identifier; it is later, so it is accepted.
        var outcome = await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(6)));

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Applied));
        Assert.That(await FindEntityAsync(), Is.Not.Null);
    }

    [Test]
    public async Task Delete_IsIgnored_ButStillRecordsTheTombstone_WhenTheEntityChangedAfterIt()
    {
        await ApplyAsync(TestCmsEvents.Publish(EntityId, version: 2, TestCmsEvents.At(5)));

        // A delete that predates the stored state: dropping the entity would lose a newer change.
        var outcome = await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(3)));
        var tombstone = await FindTombstoneAsync();

        Assert.That(outcome.Status, Is.EqualTo(CmsEventStatus.Ignored));
        Assert.That(await FindEntityAsync(), Is.Not.Null);

        // Recorded anyway, so events older than the deletion are still discarded.
        Assert.That(tombstone!.DeletedTimestamp, Is.EqualTo(TestCmsEvents.At(3)));
    }

    [Test]
    public async Task Delete_KeepsTheLatestDeletionTimestamp_WhenAnOlderDeleteArrivesAfterwards()
    {
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(5)));
        await ApplyAsync(TestCmsEvents.Delete(EntityId, TestCmsEvents.At(2)));

        var tombstone = await FindTombstoneAsync();

        Assert.That(tombstone!.DeletedTimestamp, Is.EqualTo(TestCmsEvents.At(5)));
    }

    // ------------------------------------------------------------ consistency

    [Test]
    public void ApplyAsync_Throws_WhenTheEventIsIncomplete()
    {
        var incomplete = TestCmsEvents.Publish(EntityId, version: 1, TestCmsEvents.At(0));
        incomplete.EventTimestamp = null;

        // Such an event is rejected at the webhook and never reaches the applier; if one ever did, the
        // processor must record a failure instead of writing a half-known state.
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _applier.ApplyAsync(incomplete, CancellationToken.None));
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
}
