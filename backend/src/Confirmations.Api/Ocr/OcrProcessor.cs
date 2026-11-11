using System.Globalization;
using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Confirmations.Api.Scans;
using Confirmations.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Ocr;

/// <summary>
/// Reads one uploaded scan with Tesseract and saves the result. The reading
/// is kept unchanged in OcrResults; copies go into the Pending record only as
/// suggestions (never over anything an employee typed, never on a reviewed
/// record). If reading fails, the record still waits for manual entry.
/// Afterwards the record is checked and problems are flagged (Step 6).
/// Logs never contain what was read – only the scan ID and the outcome.
/// </summary>
public class OcrProcessor(
    AppDbContext db,
    IScanStorage storage,
    TicketReader reader,
    ConfirmationValidator validator,
    TimeProvider clock,
    ILogger<OcrProcessor> logger)
{
    public async Task ProcessAsync(Guid scanId, CancellationToken cancellationToken)
    {
        var scan = await db.ScanDocuments
            .Include(s => s.OcrResult)
            .Include(s => s.Confirmation)
            .FirstOrDefaultAsync(s => s.Id == scanId, cancellationToken);
        if (scan is null || scan.OcrResult is not null)
            return; // hidden, gone, or already read

        var result = new OcrResult { ScanDocumentId = scan.Id };
        try
        {
            var path = storage.GetFullPath(scan.StoredFileName)
                ?? throw new FileNotFoundException("Stored scan file is missing.");
            var scanned = await reader.ReadAsync(path, cancellationToken);
            var r = scanned.Reading;

            result.RawText = scanned.RawText;
            result.OcrFailed = !r.LayoutFound;
            result.Zone = Fit(r.Zone.Text, 100);
            result.ZoneConfidence = r.Zone.Confidence;
            result.OrderNumber = Fit(r.OrderNumber.Text, 100);
            result.OrderNumberConfidence = r.OrderNumber.Confidence;
            result.DeliverTo = Fit(r.DeliverTo.Text, 500);
            result.DeliverToConfidence = r.DeliverTo.Confidence;
            result.TotalProducts = Fit(r.TotalProducts.Text, 50);
            result.TotalProductsConfidence = r.TotalProducts.Confidence;
            result.DeliveryInstructions = Fit(r.DeliveryInstructions.Text, 2000);
            result.DeliveryInstructionsConfidence = r.DeliveryInstructions.Confidence;
            result.SignatureMarkDetected = scanned.Signature?.Detected;
            result.SignatureConfidence = scanned.Signature?.Confidence;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.OcrFailed = true;
            logger.LogWarning("OCR could not read scan {ScanId} ({Reason}). It will go to manual entry.", scanId, ex.GetType().Name);
        }
        result.ProcessedAt = clock.GetUtcNow().UtcDateTime;
        db.OcrResults.Add(result);

        var record = scan.Confirmation;
        if (record is { Status: ConfirmationStatus.Pending, ReviewedAt: null } && !result.OcrFailed)
        {
            record.Zone ??= Fit(result.Zone, 50);
            record.OrderNumber ??= Fit(result.OrderNumber, 50);
            record.DeliverTo ??= Fit(result.DeliverTo, 300);
            record.TotalProducts ??= WholeNumber(result.TotalProducts);
            record.DeliveryInstructions ??= Fit(result.DeliveryInstructions, 1000);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone edited the record at the same moment: keep their changes,
            // save the OCR reading only.
            if (record is not null)
                await db.Entry(record).ReloadAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (record is not null)
            await validator.CheckAsync(record.Id, cancellationToken);

        logger.LogInformation("OCR finished for scan {ScanId} (failed: {Failed}).", scanId, result.OcrFailed);
    }

    private static string? Fit(string? text, int maxLength) =>
        text is null ? null : text.Length <= maxLength ? text : text[..maxLength];

    /// <summary>Only plain digits count ("4"); anything else stays empty for the employee.</summary>
    private static int? WholeNumber(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
}
