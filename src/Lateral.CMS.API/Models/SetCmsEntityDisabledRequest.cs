namespace Lateral.CMS.API.Models;

/// <summary>Body of the local admin override. The identifier comes from the route.</summary>
public class SetCmsEntityDisabledRequest
{
    /// <summary><c>true</c> hides the entity from consumers; <c>false</c> makes it visible again.</summary>
    public bool IsDisabled { get; set; }
}
