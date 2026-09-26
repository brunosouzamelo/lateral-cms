namespace Lateral.CMS.API.Models;

/// <summary>
/// Body of the local admin override. The entity is named by the route, so this carries only the decision.
/// </summary>
public class SetCmsEntityDisabledRequest
{
    /// <summary>
    /// <c>true</c> hides the entity from consumers; <c>false</c> makes it visible again. The override is
    /// local: it never reaches the CMS, it leaves the entity's payload, version and published status
    /// untouched, and a later CMS event does not undo it.
    /// </summary>
    public bool IsDisabled { get; set; }
}
