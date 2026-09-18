using System.Linq.Expressions;
using Lateral.CMS.Domain.Enumerations;
using CmsEntityEntity = Lateral.CMS.Domain.Entities.Content.CmsEntity;

namespace Lateral.CMS.Application.Content.CmsEntity.Services;

/// <summary>
/// Single visibility rule for every read. Users see published entities that were not disabled by an admin;
/// admins see everything, including entities unpublished in the CMS and entities disabled locally.
/// </summary>
public static class CmsEntityVisibility
{
    private static readonly Expression<Func<CmsEntityEntity, bool>> Everything = _ => true;

    private static readonly Expression<Func<CmsEntityEntity, bool>> Enabled =
        e => e.CmsEntityStatusId == CmsEntityStatus.Published && !e.IsDisabledByAdmin;

    public static Expression<Func<CmsEntityEntity, bool>> For(bool isAdmin) => isAdmin ? Everything : Enabled;
}
