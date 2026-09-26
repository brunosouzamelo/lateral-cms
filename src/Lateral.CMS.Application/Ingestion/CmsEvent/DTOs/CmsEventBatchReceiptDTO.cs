namespace Lateral.CMS.Application.Ingestion.CmsEvent.DTOs;

/// <summary>
/// What the webhook answers for a received batch. The batch is stored, not yet applied, so this reports
/// what was taken in — never the outcome of processing, which the event log carries afterwards.
/// </summary>
public class CmsEventBatchReceiptDTO
{
    /// <summary>
    /// Identifier given to this delivery. Pass it to <c>GET /cms/events?batchId=</c> to follow what
    /// became of the events it carried.
    /// </summary>
    public Guid BatchId { get; set; }

    /// <summary>Number of events the batch contained.</summary>
    public int Received { get; set; }

    /// <summary>
    /// Number of events stored for processing. Accepted is not applied: an accepted event may still turn
    /// out to be a duplicate or a stale delivery once it is processed.
    /// </summary>
    public int Accepted { get; set; }

    /// <summary>Number of events refused by validation. Always the length of <see cref="Rejections"/>.</summary>
    public int Rejected => Rejections.Count;

    /// <summary>
    /// The refused events and why. Every event is validated on its own, so these do not prevent the rest
    /// of the batch from being accepted and there is no reason to redeliver the whole batch.
    /// </summary>
    public List<CmsEventRejectionDTO> Rejections { get; set; } = [];
}
