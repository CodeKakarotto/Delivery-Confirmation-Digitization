namespace Confirmations.Api.Data.Entities;

/// <summary>One problem found on a record, shown in "Needs attention".</summary>
public class ValidationFlag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DeliveryConfirmationId { get; set; }
    public DeliveryConfirmation DeliveryConfirmation { get; set; } = null!;

    public FlagType Type { get; set; }

    /// <summary>Which ticket field it's about, if any (e.g. "Zone").</summary>
    public string? Field { get; set; }

    /// <summary>Plain-English explanation for employees.</summary>
    public required string Message { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
}
