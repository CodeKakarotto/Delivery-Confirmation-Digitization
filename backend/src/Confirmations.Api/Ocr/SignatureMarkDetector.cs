using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Confirmations.Api.Ocr;

/// <summary>Whether the signature area has a pen mark, and how sure we are (0–1).</summary>
public sealed record SignatureCheck(bool Detected, float Confidence);

/// <summary>
/// Looks for a pen mark in the signature area. It only measures ink; it never
/// reads, stores, or compares signatures, and is never used to identify anyone.
/// </summary>
public interface ISignatureMarkDetector
{
    /// <summary>Null if the picture can't be checked.</summary>
    SignatureCheck? Detect(string imagePath, PixelBox area);
}

/// <summary>Used where pictures can't be checked (non-Windows): always "unknown".</summary>
public sealed class NoSignatureMarkDetector : ISignatureMarkDetector
{
    public SignatureCheck? Detect(string imagePath, PixelBox area) => null;
}

/// <summary>
/// Counts dark pixels in the signature area. Printed lines (the signature
/// line, table borders) are ignored: any row or column that is mostly dark
/// is a ruled line, not handwriting.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SignatureMarkDetector : ISignatureMarkDetector
{
    /// <summary>Share of the area that must be ink to count as "signed" (0.4%).</summary>
    public const double InkThreshold = 0.004;
    private const int DarkLevel = 110;     // 0 = black, 255 = white
    private const double RuledLine = 0.4;  // a row/column this dark is a printed line

    public SignatureCheck? Detect(string imagePath, PixelBox area)
    {
        using var image = new Bitmap(imagePath);
        var box = Rectangle.Intersect(new Rectangle(area.Left, area.Top, area.Width, area.Height),
                                      new Rectangle(0, 0, image.Width, image.Height));
        if (box.Width < 10 || box.Height < 10)
            return null;

        var dark = ReadDarkPixels(image, box);
        int w = box.Width, h = box.Height;

        var rowCounts = new int[h];
        var colCounts = new int[w];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (dark[y * w + x]) { rowCounts[y]++; colCounts[x]++; }

        var lineRows = rowCounts.Select(c => c > w * RuledLine).ToArray();
        var lineCols = colCounts.Select(c => c > h * RuledLine).ToArray();

        long ink = 0, counted = 0;
        for (var y = 0; y < h; y++)
        {
            if (lineRows[y]) continue;
            for (var x = 0; x < w; x++)
            {
                if (lineCols[x]) continue;
                counted++;
                if (dark[y * w + x]) ink++;
            }
        }
        if (counted == 0)
            return null;

        var share = (double)ink / counted;
        var detected = share >= InkThreshold;
        var confidence = detected
            ? Math.Min(1.0, 0.5 + share / (InkThreshold * 10))
            : Math.Min(1.0, 0.5 + (InkThreshold - share) / InkThreshold * 0.5);
        return new SignatureCheck(detected, (float)Math.Round(confidence, 2));
    }

    private static bool[] ReadDarkPixels(Bitmap image, Rectangle box)
    {
        var data = image.LockBits(box, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * box.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var dark = new bool[box.Width * box.Height];
            for (var y = 0; y < box.Height; y++)
            {
                for (var x = 0; x < box.Width; x++)
                {
                    var i = y * data.Stride + x * 4; // B G R A
                    var brightness = (bytes[i] * 114 + bytes[i + 1] * 587 + bytes[i + 2] * 299) / 1000;
                    dark[y * box.Width + x] = brightness < DarkLevel;
                }
            }
            return dark;
        }
        finally
        {
            image.UnlockBits(data);
        }
    }
}
