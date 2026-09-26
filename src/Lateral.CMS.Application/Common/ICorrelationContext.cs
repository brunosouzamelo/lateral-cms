namespace Lateral.CMS.Application.Common;

/// <summary>
/// The identifier that ties together everything done for the current request. Supplied by the host, the
/// same way <see cref="Security.ICurrentUserService"/> is, so the application layer can record it without
/// knowing anything about HTTP.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// The identifier, or null outside a request — the background processor runs on its own and has none.
    /// </summary>
    string? CorrelationId { get; }
}
