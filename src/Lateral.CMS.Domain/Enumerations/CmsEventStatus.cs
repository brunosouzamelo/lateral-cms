namespace Lateral.CMS.Domain.Enumerations;

public enum CmsEventStatus
{
    /// <summary>Accepted and waiting to be processed.</summary>
    Pending = 1,

    /// <summary>Processed and changed the stored state.</summary>
    Applied = 2,

    /// <summary>Processed without changes: duplicate, stale or predating a deletion.</summary>
    Ignored = 3,

    /// <summary>Failed validation at the webhook; never processed.</summary>
    Rejected = 4,

    /// <summary>Processing kept failing until the maximum number of attempts was reached.</summary>
    Failed = 5
}
