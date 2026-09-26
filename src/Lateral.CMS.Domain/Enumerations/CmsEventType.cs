namespace Lateral.CMS.Domain.Enumerations;

/// <summary>
/// What the CMS reported. Sent as a string on the webhook — <c>publish</c>, <c>unPublish</c>,
/// <c>delete</c> — and matched without regard to case.
/// </summary>
public enum CmsEventType
{
    /// <summary>
    /// A version was made public. Creates the entity, or replaces its data when the event is newer than
    /// what is stored. Carries a version and a payload.
    /// </summary>
    Publish = 1,

    /// <summary>
    /// A version was withdrawn. The entity and its data are kept, but hidden from consumers. Carries a
    /// version and a payload, so a version that was never published still reaches this service.
    /// </summary>
    UnPublish = 2,

    /// <summary>
    /// The entity was removed. Hard-deleted here, leaving only a deletion marker so that events still in
    /// flight for it cannot bring it back. Carries neither version nor payload.
    /// </summary>
    Delete = 3
}
