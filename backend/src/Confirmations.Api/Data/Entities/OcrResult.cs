namespace Confirmations.Api.Data.Entities;

/// <summary>
/// What Tesseract read from one scan. These are suggestions only and are
/// never edited, so the original reading is always kept next to the
/// employee's corrections. Confidence scores run from 0 (guess) to 1 (sure).
/// </summary>
public class OcrResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ScanDocumentId { get; set; }
    public ScanDocument ScanDocument { get; set; } = null!;

    public string? Zone { get; set; }
    public float? ZoneConfidence { get; set; }

    public string? OrderNumber { get; set; }
    public float? OrderNumberConfidence { get; set; }

    public string? DeliverTo { get; set; }
    public float? DeliverToConfidence { get; set; }

    /// <summary>Kept as text exactly as read (e.g. "2" or "Z"), checked later.</summary>
    public string? TotalProducts { get; set; }
    public float? TotalProductsConfidence { get; set; }

    public string? DeliveryInstructions { get; set; }
    public float? DeliveryInstructionsConfidence { get; set; }

    /// <summary>Whether the signature box appears to have a mark. Signatures are never read or matched.</summary>
    public bool? SignatureMarkDetected { get; set; }
    public float? SignatureConfidence { get; set; }

    /// <summary>Everything Tesseract read from the page.</summary>
    public string? RawText { get; set; }

    /// <summary>True when Tesseract couldn't read the scan; the record still goes to manual review.</summary>
    public bool OcrFailed { get; set; }

    public DateTime ProcessedAt { get; set; }
}
