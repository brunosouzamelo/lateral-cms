namespace Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;

public class CmsEventRejectionDTO
{
    /// <summary>Zero-based position of the event in the received batch.</summary>
    public int Index { get; set; }
    public string? Id { get; set; }
    public List<string> Errors { get; set; } = [];
}
