namespace Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;

/// <summary>One event of the batch that failed validation, and why.</summary>
public class CmsEventRejectionDTO
{
    /// <summary>
    /// Zero-based position of the event in the received batch, so the sender can tell which one it was
    /// even when several share an identifier or none could be read.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// The <c>id</c> the event carried, when it was readable. Null when the event had none, which is one
    /// of the reasons it may have been refused.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>Every rule the event broke, not only the first one.</summary>
    public List<string> Errors { get; set; } = [];
}
