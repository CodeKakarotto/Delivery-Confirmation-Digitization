namespace Confirmations.Api.Data.Entities;

/// <summary>
/// The employee-checked version of one scanned sheet. Starts as Pending with
/// OCR suggestions copied in; becomes the official confirmation only when a
/// person approves it. One record per scan; one Approved record per order.
/// </summary>
public class DeliveryConfirmation
{
    /// <summary>The unique confirmation ID.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ScanDocumentId { get; set; }
    public ScanDocument ScanDocument { get; set; } = null!;

    /// <summary>Linked once the order number matches a known order.</summary>
    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    // The five ticket fields, as corrected by the employee.
    public string? Zone { get; set; }
    public string? OrderNumber { get; set; }
    public string? DeliverTo { get; set; }
    public int? TotalProducts { get; set; }
    public string? DeliveryInstructions { get; set; }

    /// <summary>"Signature present: yes/no", confirmed by the employee.</summary>
    public bool? SignaturePresent { get; set; }

    public ConfirmationStatus Status { get; set; } = ConfirmationStatus.Pending;

    /// <summary>Who approved or rejected it (from the server login, never the browser).</summary>
    public string? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public string? ManagerNote { get; set; }
    public string? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public string? VoidReason { get; set; }
    public string? VoidedByUserId { get; set; }
    public DateTime? VoidedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Changes on every save, so two people can't silently overwrite each other.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public List<ValidationFlag> Flags { get; set; } = [];
}
