namespace Confirmations.Api.Data.Entities;

/// <summary>
/// One uploaded scan file. The file itself lives in a private folder on the
/// host computer; the database stores only where it is and facts about it.
/// Scans are never overwritten or erased – only hidden (IsDeleted).
/// </summary>
public class ScanDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The name the file had on the employee's computer (display only).</summary>
    public required string OriginalFileName { get; set; }

    /// <summary>Random name used in the private scan folder. Unique.</summary>
    public required string StoredFileName { get; set; }

    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public int PageCount { get; set; }

    /// <summary>SHA-256 fingerprint of the file, used to spot re-uploads of the same sheet.</summary>
    public required string Sha256 { get; set; }

    public required string UploadedByUserId { get; set; }

    /// <summary>Server time (UTC), never the browser's clock.</summary>
    public DateTime UploadedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }

    public OcrResult? OcrResult { get; set; }
    public DeliveryConfirmation? Confirmation { get; set; }
}
