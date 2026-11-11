using Confirmations.Api.Data.Entities;
using Confirmations.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Scans;

/// <summary>
/// One saved scan in a list. Status (reading the sheet): "Reading", "Read", or "Failed".
/// ReviewStatus (a person's decision): "Waiting for review", "Confirmed", "Denied", …
/// Exceptions: short labels for the "Exceptions / notes" column.
/// </summary>
public sealed record ScanSummary(
    Guid ScanId, Guid ConfirmationId, string FileName, DateTime UploadedAt, string Status,
    string? Zone, string? OrderNumber, string? DeliverTo, int? TotalProducts,
    string? DeliveryInstructions, int OpenFlags,
    string ReviewStatus, IReadOnlyList<string> Exceptions);

public static class ScanSummaries
{
    public static string ReviewLabel(ConfirmationStatus status) => status switch
    {
        ConfirmationStatus.Approved => "Confirmed",
        ConfirmationStatus.Rejected => "Denied",
        ConfirmationStatus.Flagged => "Sent to manager",
        ConfirmationStatus.Voided => "Voided",
        _ => "Waiting for review",
    };

    public static string ReadLabel(OcrResult? ocr) =>
        ocr is null ? "Reading" : ocr.OcrFailed ? "Failed" : "Read";

    /// <summary>Exception labels: typed note keywords, instruction keywords, then problems found.</summary>
    public static IReadOnlyList<string> ExceptionLabels(
        string? instructions, string? notes, IEnumerable<(FlagType Type, string? Field)> openFlags) =>
        DeliveryExceptions.FromInstructions(notes)
            .Concat(DeliveryExceptions.FromInstructions(instructions))
            .Concat(openFlags.Select(f => DeliveryExceptions.LabelFor(f.Type, f.Field)))
            .Distinct()
            .ToList();

    public static async Task<List<ScanSummary>> BuildAsync(IQueryable<ScanDocument> scans, CancellationToken ct)
    {
        // Hidden (soft-deleted) scans are left out automatically.
        var rows = await scans.Select(s => new
            {
                s.Id, ConfirmationId = s.Confirmation!.Id, s.OriginalFileName, s.UploadedAt,
                Read = s.OcrResult == null ? "Reading" : s.OcrResult.OcrFailed ? "Failed" : "Read",
                s.Confirmation.Zone, s.Confirmation.OrderNumber, s.Confirmation.DeliverTo,
                s.Confirmation.TotalProducts, s.Confirmation.DeliveryInstructions, s.Confirmation.ExceptionNotes,
                s.Confirmation.Status,
                Flags = s.Confirmation.Flags.Where(f => f.ResolvedAt == null)
                    .OrderBy(f => f.CreatedAt).Select(f => new { f.Type, f.Field }).ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new ScanSummary(
                r.Id, r.ConfirmationId, r.OriginalFileName,
                // Times are stored as UTC; label them so browsers convert to local time correctly.
                DateTime.SpecifyKind(r.UploadedAt, DateTimeKind.Utc),
                r.Read, r.Zone, r.OrderNumber, r.DeliverTo, r.TotalProducts, r.DeliveryInstructions,
                r.Flags.Count,
                ReviewLabel(r.Status),
                ExceptionLabels(r.DeliveryInstructions, r.ExceptionNotes, r.Flags.Select(f => (f.Type, f.Field)))))
            .ToList();
    }
}
