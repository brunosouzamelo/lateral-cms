namespace Lateral.CMS.Domain.Enumerations;

/// <summary>
/// Where a received event ended up. <see cref="Pending"/> is the only state that is not final; the reason
/// behind any of them is reported alongside it in the event log.
/// </summary>
public enum CmsEventStatus
{
    /// <summary>Accepted and waiting to be processed.</summary>
    Pending = 1,

    /// <summary>Processed and changed the stored state.</summary>
    Applied = 2,

    /// <summary>
    /// Processed without changes, which is the normal outcome for a re-delivery: a duplicate, an event
    /// older than what is stored, or one that predates the deletion of its entity.
    /// </summary>
    Ignored = 3,

    /// <summary>Failed validation at the webhook; never processed. Reported in the batch receipt.</summary>
    Rejected = 4,

    /// <summary>
    /// Processing kept failing until the maximum number of attempts was reached. Needs attention: the
    /// stored state does not reflect this event.
    /// </summary>
    Failed = 5
}
