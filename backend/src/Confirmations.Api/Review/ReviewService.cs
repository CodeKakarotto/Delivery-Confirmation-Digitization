using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Confirmations.Api.Scans;
using Confirmations.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Review;

public sealed record OriginalReading(
    string? Zone, float? ZoneConfidence, string? OrderNumber, float? OrderNumberConfidence,
    string? DeliverTo, float? DeliverToConfidence, string? TotalProducts, float? TotalProductsConfidence,
    string? DeliveryInstructions, float? DeliveryInstructionsConfidence,
    bool? SignatureMarkDetected, float? SignatureConfidence, bool OcrFailed);

public sealed record FlagView(string Type, string? Field, string Message);

/// <summary>Everything the "Check &amp; approve" screen needs for one sheet.</summary>
public sealed record ReviewDetail(
    Guid ConfirmationId, Guid ScanId, string FileName, DateTime UploadedAt, string ReadStatus,
    string Status, string ReviewStatus, Guid Version,
    string? Zone, string? OrderNumber, string? DeliverTo, int? TotalProducts, string? DeliveryInstructions,
    bool? SignaturePresent, string? ExceptionNotes, string? ReviewerNote, string? ReviewedBy, DateTime? ReviewedAt,
    OriginalReading? Original, IReadOnlyList<FlagView> Flags, IReadOnlyList<string> Exceptions);

/// <summary>The employee's version of the fields, plus the Version they started from.</summary>
public sealed record EditRequest(
    Guid Version, string? Zone, string? OrderNumber, string? DeliverTo, int? TotalProducts,
    string? DeliveryInstructions, bool? SignaturePresent, string? ExceptionNotes);

public sealed record RejectRequest(Guid Version, string? Reason);
public sealed record ManagerRequest(Guid Version, string? Note);

public enum ReviewOutcome { Ok, NotFound, Conflict, AlreadyReviewed, Invalid }

public sealed record ReviewResult(ReviewOutcome Outcome, string? Message = null, IReadOnlyList<string>? Reasons = null)
{
    public static readonly ReviewResult Ok = new(ReviewOutcome.Ok);
}

/// <summary>
/// Check &amp; approve (Step 7). Only a person turns a sheet into a confirmation.
/// The reviewer always comes from the server login (passed in as userId).
/// Original OCR readings are never changed; every action goes in the audit log
/// (field names only, never values).
/// </summary>
public class ReviewService(AppDbContext db, ConfirmationValidator validator, TimeProvider clock)
{
    public const string ChangedBySomeoneElse = "Someone else changed this record. Please reload it to see their changes.";

    private sealed record FieldRule(string Name, string Label, int MaxLength);

    private static readonly FieldRule Zone = new(nameof(DeliveryConfirmation.Zone), "Zone", 50);
    private static readonly FieldRule OrderId = new(nameof(DeliveryConfirmation.OrderNumber), "Order ID", 50);
    private static readonly FieldRule DeliverTo = new(nameof(DeliveryConfirmation.DeliverTo), "Deliver to", 300);
    private static readonly FieldRule Instructions = new(nameof(DeliveryConfirmation.DeliveryInstructions), "Delivery instructions", 1000);
    private static readonly FieldRule Notes = new(nameof(DeliveryConfirmation.ExceptionNotes), "Exceptions / notes", 500);

    // ---------- Reading ----------

    public async Task<List<ScanSummary>> WaitingAsync(CancellationToken ct) =>
        await ScanSummaries.BuildAsync(
            db.ScanDocuments.Where(s => s.Confirmation!.Status == ConfirmationStatus.Pending)
                .OrderBy(s => s.UploadedAt).Take(500),
            ct);

    public async Task<ReviewDetail?> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await db.DeliveryConfirmations.AsNoTracking()
            .Include(x => x.ScanDocument).ThenInclude(s => s.OcrResult)
            .Include(x => x.Flags)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;

        var reviewedBy = c.ReviewedByUserId is null ? null
            : await db.Users.Where(u => u.Id == c.ReviewedByUserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        var o = c.ScanDocument.OcrResult;
        var open = c.Flags.Where(f => f.ResolvedAt == null).OrderBy(f => f.CreatedAt).ToList();

        return new ReviewDetail(
            c.Id, c.ScanDocumentId, c.ScanDocument.OriginalFileName,
            DateTime.SpecifyKind(c.ScanDocument.UploadedAt, DateTimeKind.Utc), ScanSummaries.ReadLabel(o),
            c.Status.ToString(), ScanSummaries.ReviewLabel(c.Status), c.Version,
            c.Zone, c.OrderNumber, c.DeliverTo, c.TotalProducts, c.DeliveryInstructions,
            c.SignaturePresent, c.ExceptionNotes, c.ReviewerNote, reviewedBy,
            c.ReviewedAt is null ? null : DateTime.SpecifyKind(c.ReviewedAt.Value, DateTimeKind.Utc),
            o is null ? null : new OriginalReading(
                o.Zone, o.ZoneConfidence, o.OrderNumber, o.OrderNumberConfidence, o.DeliverTo, o.DeliverToConfidence,
                o.TotalProducts, o.TotalProductsConfidence, o.DeliveryInstructions, o.DeliveryInstructionsConfidence,
                o.SignatureMarkDetected, o.SignatureConfidence, o.OcrFailed),
            open.Select(f => new FlagView(f.Type.ToString(), f.Field, f.Message)).ToList(),
            ScanSummaries.ExceptionLabels(c.DeliveryInstructions, c.ExceptionNotes, open.Select(f => (f.Type, f.Field))));
    }

    // ---------- Actions ----------

    public async Task<ReviewResult> SaveAsync(Guid id, EditRequest request, string userId, CancellationToken ct)
    {
        var (record, problem) = await LoadForReviewAsync(id, request.Version, ct);
        if (problem is not null) return problem;

        var reasons = LengthProblems(request);
        if (reasons.Count > 0) return new(ReviewOutcome.Invalid, "Please fix these first.", reasons);

        ApplyEdits(record!, request, userId);
        var saved = await SaveAsync(ct);
        if (saved is not null) return saved;

        await validator.CheckAsync(id, ct); // refresh the problem flags
        return ReviewResult.Ok;
    }

    public async Task<ReviewResult> ApproveAsync(Guid id, EditRequest request, string userId, CancellationToken ct)
    {
        var (record, problem) = await LoadForReviewAsync(id, request.Version, ct);
        if (problem is not null) return problem;

        var reasons = LengthProblems(request);
        if (reasons.Count > 0) return new(ReviewOutcome.Invalid, "Please fix these first.", reasons);

        var r = record!;
        ApplyEdits(r, request, userId);

        // Everything a confirmation must have. Nothing is saved unless all of it is true.
        reasons = [];
        foreach (var (field, value) in new[] { (Zone, r.Zone), (OrderId, r.OrderNumber), (DeliverTo, r.DeliverTo) })
            if (string.IsNullOrWhiteSpace(value)) reasons.Add($"{field.Label} is missing.");
        if (r.TotalProducts is null) reasons.Add("Total products is missing.");
        else if (r.TotalProducts <= 0) reasons.Add("Total products must be a whole number above 0.");
        if (string.IsNullOrWhiteSpace(r.DeliveryInstructions)) reasons.Add("Delivery instructions is missing.");

        r.OrderId = null;
        if (!string.IsNullOrWhiteSpace(r.OrderNumber))
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == r.OrderNumber, ct);
            if (order is null)
                reasons.Add($"Order ID {r.OrderNumber} isn't in the order list.");
            else if (await db.DeliveryConfirmations.AnyAsync(c => c.Id != r.Id && c.OrderId == order.Id && c.Status == ConfirmationStatus.Approved, ct))
                reasons.Add($"Order {order.OrderNumber} already has an approved confirmation.");
            else
                r.OrderId = order.Id;
        }

        if (r.SignaturePresent is null)
            reasons.Add("Please answer \"Signature present\": yes or no.");
        else if (r.SignaturePresent == false && string.IsNullOrWhiteSpace(r.ExceptionNotes))
            reasons.Add("No signature: please add a note in Exceptions / notes (for example, \"Left at door\").");

        if (reasons.Count > 0)
            return new(ReviewOutcome.Invalid, "This sheet can't be approved yet.", reasons);

        var now = Now;
        r.Status = ConfirmationStatus.Approved;
        r.ReviewedByUserId = userId;
        r.ReviewedAt = now;
        await ResolveOpenFlagsAsync(r.Id, userId, "Approved.", ct);
        Audit(userId, AuditAction.Approved, r.Id);

        return await SaveAsync(ct, orderTakenMessage: $"Order {r.OrderNumber} already has an approved confirmation.") ?? ReviewResult.Ok;
    }

    public async Task<ReviewResult> RejectAsync(Guid id, RejectRequest request, string userId, CancellationToken ct)
    {
        var (record, problem) = await LoadForReviewAsync(id, request.Version, ct);
        if (problem is not null) return problem;

        var reason = Tidy(request.Reason);
        if (reason?.Length > 500)
            return new(ReviewOutcome.Invalid, "Please fix these first.", ["The reason can be at most 500 characters."]);

        var r = record!;
        r.Status = ConfirmationStatus.Rejected;
        r.ReviewerNote = reason;
        r.ReviewedByUserId = userId;
        r.ReviewedAt = Now;
        await ResolveOpenFlagsAsync(r.Id, userId, "Rejected.", ct);
        Audit(userId, AuditAction.Rejected, r.Id);
        return await SaveAsync(ct) ?? ReviewResult.Ok;
    }

    public async Task<ReviewResult> SendToManagerAsync(Guid id, ManagerRequest request, string userId, CancellationToken ct)
    {
        var (record, problem) = await LoadForReviewAsync(id, request.Version, ct);
        if (problem is not null) return problem;

        var note = Tidy(request.Note);
        if (note is null)
            return new(ReviewOutcome.Invalid, "Please fix these first.", ["Please write a note for the manager."]);
        if (note.Length > 500)
            return new(ReviewOutcome.Invalid, "Please fix these first.", ["The note can be at most 500 characters."]);

        var r = record!;
        r.Status = ConfirmationStatus.Flagged; // problems stay open for the manager
        r.ReviewerNote = note;
        Audit(userId, AuditAction.SentToManager, r.Id);
        return await SaveAsync(ct) ?? ReviewResult.Ok;
    }

    // ---------- Helpers ----------

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<(DeliveryConfirmation? Record, ReviewResult? Problem)> LoadForReviewAsync(Guid id, Guid version, CancellationToken ct)
    {
        var record = await db.DeliveryConfirmations.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (record is null)
            return (null, new(ReviewOutcome.NotFound));
        if (record.Status != ConfirmationStatus.Pending)
            return (null, new(ReviewOutcome.AlreadyReviewed, $"This sheet has already been reviewed ({ScanSummaries.ReviewLabel(record.Status)})."));
        if (record.Version != version)
            return (null, new(ReviewOutcome.Conflict, ChangedBySomeoneElse));

        // The database also checks this version when saving, in case two saves race.
        db.Entry(record).Property(c => c.Version).OriginalValue = version;
        return (record, null);
    }

    private static List<string> LengthProblems(EditRequest e)
    {
        var reasons = new List<string>();
        foreach (var (rule, value) in new[] { (Zone, e.Zone), (OrderId, e.OrderNumber), (DeliverTo, e.DeliverTo), (Instructions, e.DeliveryInstructions), (Notes, e.ExceptionNotes) })
            if (Tidy(value)?.Length > rule.MaxLength)
                reasons.Add($"{rule.Label} can be at most {rule.MaxLength} characters.");
        if (e.TotalProducts is < 0 or > 10000)
            reasons.Add("Total products must be a whole number above 0.");
        return reasons;
    }

    /// <summary>Copies the employee's values in and records which fields changed (names only).</summary>
    private void ApplyEdits(DeliveryConfirmation r, EditRequest e, string userId)
    {
        var changed = new List<string>();
        void Set<T>(string name, T current, T next, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, next)) return;
            assign(next);
            changed.Add(name);
        }

        var orderNumber = Tidy(e.OrderNumber)?.ToUpperInvariant();
        Set(Zone.Name, r.Zone, Tidy(e.Zone), v => r.Zone = v);
        Set(OrderId.Name, r.OrderNumber, orderNumber, v => r.OrderNumber = v);
        Set(DeliverTo.Name, r.DeliverTo, Tidy(e.DeliverTo), v => r.DeliverTo = v);
        Set(nameof(DeliveryConfirmation.TotalProducts), r.TotalProducts, e.TotalProducts, v => r.TotalProducts = v);
        Set(Instructions.Name, r.DeliveryInstructions, Tidy(e.DeliveryInstructions), v => r.DeliveryInstructions = v);
        Set(nameof(DeliveryConfirmation.SignaturePresent), r.SignaturePresent, e.SignaturePresent, v => r.SignaturePresent = v);
        Set(Notes.Name, r.ExceptionNotes, Tidy(e.ExceptionNotes), v => r.ExceptionNotes = v);

        if (changed.Count > 0)
            Audit(userId, AuditAction.Edited, r.Id, string.Join(',', changed));
    }

    private async Task ResolveOpenFlagsAsync(Guid id, string userId, string note, CancellationToken ct)
    {
        foreach (var flag in await db.ValidationFlags.Where(f => f.DeliveryConfirmationId == id && f.ResolvedAt == null).ToListAsync(ct))
        {
            flag.ResolvedAt = Now;
            flag.ResolvedByUserId = userId;
            flag.ResolutionNote = note;
        }
    }

    private void Audit(string userId, AuditAction action, Guid id, string? changedFields = null) =>
        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = userId, Action = action, EntityType = nameof(DeliveryConfirmation),
            EntityId = id.ToString(), ChangedFields = changedFields, Timestamp = Now,
        });

    private async Task<ReviewResult?> SaveAsync(CancellationToken ct, string? orderTakenMessage = null)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ReviewOutcome.Conflict, ChangedBySomeoneElse);
        }
        catch (DbUpdateException) when (orderTakenMessage is not null)
        {
            // The database's own "one approved confirmation per order" rule.
            return new(ReviewOutcome.Invalid, "This sheet can't be approved yet.", [orderTakenMessage]);
        }
    }

    private static string? Tidy(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
