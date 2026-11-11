using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Validation;

/// <summary>
/// Checks a waiting (Pending) record and keeps its flags up to date. Flags
/// only point out problems for a person; they never approve, reject, or change
/// a record's status. Fixed problems are marked resolved, never deleted.
/// Also links the record to its order when the order ID matches one.
/// </summary>
public class ConfirmationValidator(AppDbContext db, TimeProvider clock)
{
    /// <summary>Fields read with less confidence than this get a "please double-check" flag.</summary>
    public const float LowConfidenceBelow = 0.80f;

    private sealed record Problem(FlagType Type, string? Field, string Message);

    private sealed record FieldInfo(string Name, string Label, string Phrase);

    private static readonly FieldInfo Zone = new(nameof(DeliveryConfirmation.Zone), "Zone", "the zone");
    private static readonly FieldInfo OrderId = new(nameof(DeliveryConfirmation.OrderNumber), "Order ID", "the order ID");
    private static readonly FieldInfo DeliverTo = new(nameof(DeliveryConfirmation.DeliverTo), "Deliver to", "the name and address");
    private static readonly FieldInfo Total = new(nameof(DeliveryConfirmation.TotalProducts), "Total products", "the number of products");
    private static readonly FieldInfo Instructions = new(nameof(DeliveryConfirmation.DeliveryInstructions), "Delivery instructions", "the delivery instructions");

    /// <summary>Checks every waiting record that has been read (used at startup).</summary>
    public async Task CheckAllWaitingAsync(CancellationToken cancellationToken)
    {
        var ids = await db.DeliveryConfirmations
            .Where(c => c.Status == ConfirmationStatus.Pending && c.ScanDocument.OcrResult != null)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in ids)
            await CheckOneAsync(id, cancellationToken);
    }

    /// <summary>
    /// Checks one record, then re-checks other waiting sheets for the same order
    /// (before and after any order-ID change) so "another sheet for this order"
    /// shows on every one of them straight away.
    /// </summary>
    public async Task CheckAsync(Guid confirmationId, CancellationToken cancellationToken)
    {
        var (orderBefore, orderAfter) = await CheckOneAsync(confirmationId, cancellationToken);

        foreach (var orderId in new[] { orderBefore, orderAfter }.Where(o => o is not null).Distinct())
        {
            var others = await db.DeliveryConfirmations
                .Where(c => c.Id != confirmationId && c.OrderId == orderId && c.Status == ConfirmationStatus.Pending)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            foreach (var other in others)
                await CheckOneAsync(other, cancellationToken);
        }
    }

    private async Task<(int? OrderBefore, int? OrderAfter)> CheckOneAsync(Guid confirmationId, CancellationToken cancellationToken)
    {
        var record = await db.DeliveryConfirmations
            .Include(c => c.ScanDocument).ThenInclude(s => s.OcrResult)
            .Include(c => c.Flags)
            .FirstOrDefaultAsync(c => c.Id == confirmationId, cancellationToken);

        // Only records still waiting for review, and only once the sheet has been read.
        if (record is not { Status: ConfirmationStatus.Pending } || record.ScanDocument.OcrResult is not { } ocr)
            return (null, null);

        var orderBefore = record.OrderId;
        var problems = await FindProblemsAsync(record, ocr, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        // The duplicate-file flag comes from the upload and is handled by a person.
        var open = record.Flags.Where(f => f.ResolvedAt == null && f.Type != FlagType.DuplicateFile).ToList();
        foreach (var flag in open.Where(f => !problems.Any(p => p.Type == f.Type && p.Field == f.Field)))
        {
            flag.ResolvedAt = now;
            flag.ResolutionNote = "Fixed. Checked again automatically.";
        }
        foreach (var problem in problems)
        {
            var existing = open.FirstOrDefault(f => f.Type == problem.Type && f.Field == problem.Field);
            if (existing is not null)
                existing.Message = problem.Message;
            else
                // Added explicitly: the ID is set in advance, so EF would otherwise
                // treat it as an existing flag to update.
                db.ValidationFlags.Add(new ValidationFlag
                {
                    DeliveryConfirmationId = record.Id,
                    Type = problem.Type, Field = problem.Field, Message = problem.Message, CreatedAt = now,
                });
        }

        await db.SaveChangesAsync(cancellationToken);
        return (orderBefore, record.OrderId);
    }

    private async Task<List<Problem>> FindProblemsAsync(DeliveryConfirmation r, OcrResult ocr, CancellationToken ct)
    {
        var problems = new List<Problem>();

        var nothingEntered = Blank(r.Zone) && Blank(r.OrderNumber) && Blank(r.DeliverTo)
                             && r.TotalProducts is null && Blank(r.DeliveryInstructions);
        if (ocr.OcrFailed && nothingEntered)
        {
            // One clear message instead of five "missing" ones.
            r.OrderId = null;
            problems.Add(new(FlagType.OcrFailed, null, "This sheet couldn't be read automatically. Please type in the details."));
            return problems;
        }

        // Missing fields
        foreach (var (field, value) in new[] { (Zone, r.Zone), (OrderId, r.OrderNumber), (DeliverTo, r.DeliverTo), (Instructions, r.DeliveryInstructions) })
            if (Blank(value))
                problems.Add(Missing(field));

        // Total products: a whole number above 0
        if (r.TotalProducts is null)
            problems.Add(Blank(ocr.TotalProducts)
                ? Missing(Total)
                : new(FlagType.InvalidTotalProducts, Total.Name,
                    $"Total products must be a whole number above 0 (the sheet shows \"{ocr.TotalProducts!.Trim()}\")."));
        else if (r.TotalProducts <= 0)
            problems.Add(new(FlagType.InvalidTotalProducts, Total.Name, "Total products must be a whole number above 0."));

        // Order ID: known order, not already confirmed, not waiting twice
        r.OrderId = null;
        if (!Blank(r.OrderNumber))
        {
            var orderNumber = r.OrderNumber!.Trim().ToUpperInvariant();
            var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct);
            if (order is null)
            {
                problems.Add(new(FlagType.OrderNotFound, OrderId.Name, $"Order ID {orderNumber} isn't in the order list."));
            }
            else
            {
                r.OrderId = order.Id;
                var others = db.DeliveryConfirmations.Where(c => c.Id != r.Id && c.OrderId == order.Id);
                if (await others.AnyAsync(c => c.Status == ConfirmationStatus.Approved, ct))
                    problems.Add(new(FlagType.OrderAlreadyConfirmed, OrderId.Name,
                        $"Order {order.OrderNumber} already has an approved confirmation."));
                else if (await others.AnyAsync(c => c.Status == ConfirmationStatus.Pending || c.Status == ConfirmationStatus.Flagged, ct))
                    problems.Add(new(FlagType.DuplicateOrder, OrderId.Name,
                        $"Another sheet for order {order.OrderNumber} is waiting for review."));
            }
        }

        // Signature: the employee's answer wins; otherwise what the scan showed.
        var signed = r.SignaturePresent ?? ocr.SignatureMarkDetected;
        if (signed == false)
            problems.Add(new(FlagType.MissingSignature, null, "No signature mark was found in the Received by box."));

        // Hard-to-read fields that nobody has corrected yet
        AddIfHardToRead(problems, Zone, r.Zone, ocr.Zone, ocr.ZoneConfidence);
        AddIfHardToRead(problems, OrderId, r.OrderNumber, ocr.OrderNumber, ocr.OrderNumberConfidence);
        AddIfHardToRead(problems, DeliverTo, r.DeliverTo, ocr.DeliverTo, ocr.DeliverToConfidence);
        AddIfHardToRead(problems, Total, r.TotalProducts?.ToString(), ocr.TotalProducts, ocr.TotalProductsConfidence);
        AddIfHardToRead(problems, Instructions, r.DeliveryInstructions, ocr.DeliveryInstructions, ocr.DeliveryInstructionsConfidence);

        return problems;
    }

    private static void AddIfHardToRead(List<Problem> problems, FieldInfo field, string? current, string? asRead, float? confidence)
    {
        if (Blank(current) || confidence is null || confidence >= LowConfidenceBelow)
            return;
        if (!string.Equals(current!.Trim(), asRead?.Trim(), StringComparison.OrdinalIgnoreCase))
            return; // someone already changed it
        problems.Add(new(FlagType.LowConfidence, field.Name,
            $"Please double-check {field.Phrase}. That part of the scan was hard to read."));
    }

    private static Problem Missing(FieldInfo field) => new(FlagType.MissingField, field.Name, $"{field.Label} is missing.");

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
