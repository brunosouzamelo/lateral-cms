namespace Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;

public class CmsEventBatchReceiptDTO
{
    public Guid BatchId { get; set; }
    public int Received { get; set; }
    public int Accepted { get; set; }
    public int Rejected => Rejections.Count;
    public List<CmsEventRejectionDTO> Rejections { get; set; } = [];
}
