namespace Confirmations.Api.Ocr;

/// <summary>Settings from the "Ocr" configuration section.</summary>
public class OcrOptions
{
    /// <summary>Where tesseract.exe is installed on the host computer.</summary>
    public string TesseractPath { get; set; } = @"C:\Program Files\Tesseract-OCR\tesseract.exe";

    /// <summary>Give up on one sheet after this long, so a stuck run can't block the queue.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Read new uploads automatically in the background (switched off in most tests).</summary>
    public bool RunInBackground { get; set; } = true;
}
