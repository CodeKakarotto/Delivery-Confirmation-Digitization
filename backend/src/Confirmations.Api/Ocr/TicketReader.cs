namespace Confirmations.Api.Ocr;

public sealed record TicketScanResult(TicketReading Reading, string RawText, SignatureCheck? Signature);

/// <summary>
/// Reads one ticket picture: clean a temporary copy → OCR → five fields →
/// signature mark check (on the original picture).
/// </summary>
public class TicketReader(ITesseractRunner tesseract, ISignatureMarkDetector signatures, IImageCleaner? cleaner = null)
{
    /// <summary>Private temporary folder for cleaned copies; each is deleted right after reading.</summary>
    private static readonly string WorkFolder = Path.Combine(Path.GetTempPath(), "MichaelsFlowersOcr");

    public async Task<TicketScanResult> ReadAsync(string imagePath, CancellationToken cancellationToken)
    {
        var cleaned = cleaner?.Clean(imagePath, WorkFolder);
        List<OcrWord> words;
        try
        {
            words = TesseractTsv.Parse(await tesseract.RunTsvAsync(cleaned ?? imagePath, cancellationToken));
        }
        finally
        {
            if (cleaned is not null) File.Delete(cleaned);
        }

        var reading = TicketFieldExtractor.Extract(words);
        var signature = reading.SignatureArea is { } area ? signatures.Detect(imagePath, area) : null;
        return new TicketScanResult(reading, TesseractTsv.ToRawText(words), signature);
    }
}
