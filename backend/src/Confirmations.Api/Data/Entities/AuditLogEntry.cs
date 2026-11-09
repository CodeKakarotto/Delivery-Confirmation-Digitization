namespace Confirmations.Api.Data.Entities;

/// <summary>
/// Who did what, and when. Records which fields changed but never their
/// values, so names, addresses, and instructions never end up in the log.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }

    public string? UserId { get; set; }
    public AuditAction Action { get; set; }

    /// <summary>e.g. "DeliveryConfirmation" or "ScanDocument".</summary>
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }

    /// <summary>Comma-separated field names, e.g. "Zone,TotalProducts". No values.</summary>
    public string? ChangedFields { get; set; }

    /// <summary>Server time (UTC).</summary>
    public DateTime Timestamp { get; set; }
}
