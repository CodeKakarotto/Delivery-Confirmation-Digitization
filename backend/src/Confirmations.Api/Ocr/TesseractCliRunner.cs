using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Confirmations.Api.Ocr;

/// <summary>Runs OCR on one picture and returns Tesseract's TSV output.</summary>
public interface ITesseractRunner
{
    Task<string> RunTsvAsync(string imagePath, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the Tesseract command-line program installed on the host computer.
/// Scans never leave this computer. Arguments are passed as a list (never
/// through a command shell), so a file name can't be turned into a command.
/// </summary>
public class TesseractCliRunner(IOptions<OcrOptions> options) : ITesseractRunner
{
    public async Task<string> RunTsvAsync(string imagePath, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var start = new ProcessStartInfo(settings.TesseractPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        // <image> stdout -l eng --psm 11 tsv   (psm 11 = find scattered text, good for table cells)
        foreach (var argument in new[] { imagePath, "stdout", "-l", "eng", "--psm", "11", "tsv" })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Tesseract could not be started.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token); // drained, never logged

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await errors;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Tesseract took too long to read the sheet.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Tesseract exited with code {process.ExitCode}.");

        return await output;
    }
}
