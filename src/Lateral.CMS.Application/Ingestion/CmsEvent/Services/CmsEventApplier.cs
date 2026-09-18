using Lateral.CMS.Domain.Entities.Content;
using Lateral.CMS.Domain.Enumerations;
using CmsEventEntity = Lateral.CMS.Domain.Entities.Ingestion.CmsEvent;

namespace Lateral.CMS.Application.Ingestion.CmsEvent.Services;

public sealed record CmsEventOutcome(CmsEventStatus Status, string Reason)
{
    public static CmsEventOutcome Applied(string reason) => new(CmsEventStatus.Applied, reason);
    public static CmsEventOutcome Ignored(string reason) => new(CmsEventStatus.Ignored, reason);
}

/// <summary>
/// Applies one accepted event to the stored entities. Only stages changes on the context: the caller saves
/// them together with the event status, so the entity change and the inbox update commit atomically.
/// </summary>
/// <remarks>
/// Rules (all of them make processing idempotent, so re-deliveries and retries are safe):
/// <list type="bullet">
/// <item><b>publish</b> — creates the entity or replaces its data when the event is newer (see <see cref="CmsEventOrdering"/>).</item>
/// <item><b>unPublish</b> — keeps the data but disables the entity. The event carries the entity fields, so when its
/// version is newer than the stored one (version X published, X+1 never published, then unpublished) the stored data
/// advances to that version: the database always holds the latest version known. An unPublish for an entity never
/// stored creates it already unpublished.</item>
/// <item><b>delete</b> — hard-deletes the entity and records a tombstone (identifier + deletion timestamp only). Events
/// whose timestamp is not later than the tombstone are discarded, so late deliveries cannot resurrect deleted data,
/// while a genuine re-creation (later timestamp) is accepted. A delete older than the stored state is stale.</item>
/// </list>
/// </remarks>
public class CmsEventApplier(ICmsDbContext context, IDateTimeService dateTimeService)
{
    public async Task<CmsEventOutcome> ApplyAsync(CmsEventEntity cmsEvent, CancellationToken cancellationToken)
    {
        if (cmsEvent.CmsEventTypeId is null || cmsEvent.ExternalId is null || cmsEvent.EventTimestamp is null)
            throw new InvalidOperationException($"CMS event {cmsEvent.CmsEventId} is incomplete and cannot be applied.");

        var externalId = cmsEvent.ExternalId;
        var timestamp = cmsEvent.EventTimestamp.Value;

        var entity = await context.CmsEntity.FirstOrDefaultAsync(e => e.ExternalId == externalId, cancellationToken);
        var tombstone = await context.CmsEntityTombstone.FirstOrDefaultAsync(t => t.ExternalId == externalId, cancellationToken);

        return cmsEvent.CmsEventTypeId.Value switch
        {
            CmsEventType.Publish or CmsEventType.UnPublish => ApplyVersioned(cmsEvent, timestamp, entity, tombstone),
            CmsEventType.Delete => ApplyDelete(externalId, timestamp, entity, tombstone),
            _ => throw new InvalidOperationException($"CMS event type {cmsEvent.CmsEventTypeId} is not supported.")
        };
    }

    private CmsEventOutcome ApplyVersioned(CmsEventEntity cmsEvent, DateTimeOffset timestamp, CmsEntity? entity, CmsEntityTombstone? tombstone)
    {
        if (cmsEvent.Version is null || cmsEvent.Payload is null)
            throw new InvalidOperationException($"CMS event {cmsEvent.CmsEventId} has no version or payload.");

        var version = cmsEvent.Version.Value;
        var isPublish = cmsEvent.CmsEventTypeId == CmsEventType.Publish;
        var targetStatus = isPublish ? CmsEntityStatus.Published : CmsEntityStatus.Unpublished;
        var now = dateTimeService.UtcNowOffset;

        if (tombstone is not null && timestamp <= tombstone.DeletedTimestamp)
            return CmsEventOutcome.Ignored($"Event predates the deletion of the entity at {tombstone.DeletedTimestamp:O}.");

        if (entity is null)
        {
            context.CmsEntity.Add(new CmsEntity
            {
                CmsEntityId = Guid.CreateVersion7(),
                ExternalId = cmsEvent.ExternalId!,
                Payload = cmsEvent.Payload,
                Version = version,
                LastPublishedVersion = isPublish ? version : null,
                CmsEntityStatusId = targetStatus,
                LastEventTimestamp = timestamp,
                ConcurrencyToken = Guid.NewGuid(),
                AddedDate = now
            });

            return CmsEventOutcome.Applied(isPublish
                ? $"Entity created as published at version {version}."
                : $"Entity created as unpublished at version {version} (no earlier version was stored).");
        }

        switch (CmsEventOrdering.Compare(version, timestamp, targetStatus, entity))
        {
            case CmsEventOrder.Duplicate:
                return CmsEventOutcome.Ignored($"Duplicate event: version {version} at {timestamp:O} is already applied.");

            case CmsEventOrder.Stale:
                return CmsEventOutcome.Ignored(
                    $"Stale event: version {version} at {timestamp:O} is older than the stored version {entity.Version} at {entity.LastEventTimestamp:O}.");
        }

        var storedVersion = entity.Version;

        entity.Payload = cmsEvent.Payload;
        entity.Version = version;
        entity.CmsEntityStatusId = targetStatus;
        entity.LastEventTimestamp = timestamp;
        entity.ModifiedDate = now;
        entity.ConcurrencyToken = Guid.NewGuid();

        if (isPublish)
        {
            entity.LastPublishedVersion = version;
            return CmsEventOutcome.Applied($"Entity published at version {version} (stored version was {storedVersion}).");
        }

        return CmsEventOutcome.Applied(version > storedVersion
            ? $"Entity unpublished at version {version}; stored data advanced from version {storedVersion}, which was the last one received."
            : $"Entity unpublished at version {version}.");
    }

    private CmsEventOutcome ApplyDelete(string externalId, DateTimeOffset timestamp, CmsEntity? entity, CmsEntityTombstone? tombstone)
    {
        if (entity is null && tombstone is not null && timestamp <= tombstone.DeletedTimestamp)
            return CmsEventOutcome.Ignored($"Entity was already deleted at {tombstone.DeletedTimestamp:O}.");

        RecordTombstone(externalId, timestamp, tombstone);

        if (entity is null)
            return CmsEventOutcome.Applied("Entity is not stored; deletion recorded so older events for it are discarded.");

        if (timestamp < entity.LastEventTimestamp)
            return CmsEventOutcome.Ignored(
                $"Stale delete: the entity changed at {entity.LastEventTimestamp:O}, after the deletion at {timestamp:O}. Deletion recorded for older events.");

        context.CmsEntity.Remove(entity);

        return CmsEventOutcome.Applied($"Entity hard-deleted (stored version was {entity.Version}).");
    }

    private void RecordTombstone(string externalId, DateTimeOffset timestamp, CmsEntityTombstone? tombstone)
    {
        if (tombstone is null)
        {
            context.CmsEntityTombstone.Add(new CmsEntityTombstone
            {
                ExternalId = externalId,
                DeletedTimestamp = timestamp,
                AddedDate = dateTimeService.UtcNowOffset
            });
        }
        else if (timestamp > tombstone.DeletedTimestamp)
        {
            tombstone.DeletedTimestamp = timestamp;
        }
    }
}
