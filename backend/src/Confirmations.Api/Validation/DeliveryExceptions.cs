using System.Text.RegularExpressions;
using Confirmations.Api.Data.Entities;

namespace Confirmations.Api.Validation;

/// <summary>
/// Short labels for the "Exceptions / notes" column: delivery exceptions the
/// driver wrote in the instructions (e.g. "Left with neighbor"), and short
/// names for problems the checks found (e.g. "No signature").
/// </summary>
public static partial class DeliveryExceptions
{
    // Things that *happened* on the delivery. "Leave at side door" is an
    // instruction, not an exception, so only past-tense wording matches.
    private static readonly (string Label, Regex Pattern)[] Spotted =
    [
        ("Left at door", LeftAtDoor()),
        ("Left with neighbor", LeftWithNeighbor()),
        ("Nobody home", NobodyHome()),
        ("Refused", Refused()),
        ("Returned to shop", Returned()),
    ];

    public static IReadOnlyList<string> FromInstructions(string? instructions) =>
        string.IsNullOrWhiteSpace(instructions)
            ? []
            : Spotted.Where(s => s.Pattern.IsMatch(instructions)).Select(s => s.Label).ToList();

    public static string LabelFor(FlagType type, string? field) => type switch
    {
        FlagType.MissingField => $"{FieldLabel(field)} missing",
        FlagType.OrderNotFound => "Order not found",
        FlagType.OrderAlreadyConfirmed => "Already confirmed",
        FlagType.DuplicateOrder => "Duplicate order",
        FlagType.DuplicateFile => "Uploaded twice",
        FlagType.InvalidTotalProducts => "Bad product count",
        FlagType.UnknownZone => "Unknown zone",
        FlagType.MissingSignature => "No signature",
        FlagType.LowConfidence => $"Check {FieldLabel(field).ToLowerInvariant()}",
        FlagType.OcrFailed => "Couldn't read",
        _ => type.ToString(),
    };

    private static string FieldLabel(string? field) => field switch
    {
        nameof(DeliveryConfirmation.Zone) => "Zone",
        nameof(DeliveryConfirmation.OrderNumber) => "Order ID",
        nameof(DeliveryConfirmation.DeliverTo) => "Deliver to",
        nameof(DeliveryConfirmation.TotalProducts) => "Total products",
        nameof(DeliveryConfirmation.DeliveryInstructions) => "Delivery instructions",
        _ => "Field",
    };

    [GeneratedRegex(@"\bleft\b(\s+\w+){0,2}\s+(door|porch|step|steps|doorstep)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeftAtDoor();

    [GeneratedRegex(@"\bleft\s+with\s+(a\s+|the\s+)?neighbou?r", RegexOptions.IgnoreCase)]
    private static partial Regex LeftWithNeighbor();

    [GeneratedRegex(@"\b(nobody|no\s*one|no-one)\s+(was\s+)?home\b|\bnot\s+home\b", RegexOptions.IgnoreCase)]
    private static partial Regex NobodyHome();

    [GeneratedRegex(@"\brefused\b", RegexOptions.IgnoreCase)]
    private static partial Regex Refused();

    [GeneratedRegex(@"\breturned\b", RegexOptions.IgnoreCase)]
    private static partial Regex Returned();
}
