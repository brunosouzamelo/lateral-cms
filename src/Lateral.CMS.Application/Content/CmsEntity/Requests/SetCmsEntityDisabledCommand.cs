namespace Lateral.CMS.Application.Content.CmsEntity.Requests;

/// <summary>
/// Local admin override. Never changes CMS data (payload, version, status) and survives later CMS events;
/// a CMS delete removes the entity together with the override.
/// </summary>
public class SetCmsEntityDisabledCommand : IRequest<IResult>
{
    public required string Id { get; set; }
    public bool IsDisabled { get; set; }
}
