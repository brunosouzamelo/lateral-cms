namespace Lateral.CMS.Domain.Enumerations;

/// <summary>
/// What the CMS has decided about an entity. This mirrors the CMS and is changed only by an event from
/// it; the local admin override is a separate field and does not appear here.
/// </summary>
public enum CmsEntityStatus
{
    /// <summary>Public in the CMS. Visible to every authenticated consumer.</summary>
    Published = 1,

    /// <summary>
    /// Withdrawn in the CMS. The data is kept here — unpublishing disables an entity, it does not delete
    /// it — but only administrators can see it.
    /// </summary>
    Unpublished = 2
}
