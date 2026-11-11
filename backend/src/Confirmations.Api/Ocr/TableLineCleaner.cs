using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Confirmations.Api.Ocr;

/// <summary>Makes a cleaned copy of a picture that is easier for OCR to read.</summary>
public interface IImageCleaner
{
    /// <summary>Path of the cleaned copy (in <paramref name="workFolder"/>), or null if it couldn't be made.</summary>
    string? Clean(string imagePath, string workFolder);
}

/// <summary>
/// Turns the picture pure black-and-white and erases long straight lines
/// (table borders, the signature line). Blurry borders otherwise smear into
/// the column headings and nearby values so OCR can't read them. The original
/// scan is never changed; the cleaned copy is temporary.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TableLineCleaner : IImageCleaner
{
    public string? Clean(string imagePath, string workFolder)
    {
        try
        {
            using var image = new Bitmap(imagePath);
            int w = image.Width, h = image.Height;
            var gray = ReadGray(image);
            var threshold = OtsuThreshold(gray);
            var dark = gray.Select(v => v < threshold).ToArray();

            // A "line" is a straight run of dark pixels much longer than any letter.
            var minAcross = Math.Max(80, (int)(w * 0.08));
            var minDown = Math.Max(80, (int)(h * 0.2));
            var erase = new bool[w * h];
            for (var y = 0; y < h; y++)
                MarkRuns(dark, erase, start: y * w, step: 1, count: w, minRun: minAcross);
            for (var x = 0; x < w; x++)
                MarkRuns(dark, erase, start: x, step: w, count: h, minRun: minDown);

            using var cleaned = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = cleaned.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[data.Stride * h];
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var i = y * w + x;
                        var value = dark[i] && !erase[i] ? (byte)0 : (byte)255;
                        var o = y * data.Stride + x * 4;
                        bytes[o] = bytes[o + 1] = bytes[o + 2] = value;
                        bytes[o + 3] = 255;
                    }
                }
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally
            {
                cleaned.UnlockBits(data);
            }

            Directory.CreateDirectory(workFolder);
            var output = Path.Combine(workFolder, $"{Guid.NewGuid():N}.png");
            cleaned.Save(output, ImageFormat.Png);
            return output;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException or IOException)
        {
            return null; // not a readable picture: OCR will try the original instead
        }
    }

    private static void MarkRuns(bool[] dark, bool[] erase, int start, int step, int count, int minRun)
    {
        var runStart = -1;
        for (var k = 0; k <= count; k++)
        {
            var isDark = k < count && dark[start + k * step];
            if (isDark && runStart < 0) runStart = k;
            if (!isDark && runStart >= 0)
            {
                if (k - runStart >= minRun)
                    for (var j = runStart; j < k; j++) erase[start + j * step] = true;
                runStart = -1;
            }
        }
    }

    private static byte[] ReadGray(Bitmap image)
    {
        int w = image.Width, h = image.Height;
        var data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var gray = new byte[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * data.Stride + x * 4; // B G R A
                    gray[y * w + x] = (byte)((bytes[i] * 114 + bytes[i + 1] * 587 + bytes[i + 2] * 299) / 1000);
                }
            return gray;
        }
        finally
        {
            image.UnlockBits(data);
        }
    }

    /// <summary>Picks the brightness that best separates ink from paper (Otsu's method).</summary>
    private static int OtsuThreshold(byte[] gray)
    {
        var histogram = new long[256];
        foreach (var v in gray) histogram[v]++;
        long total = gray.Length, sumAll = 0;
        for (var i = 0; i < 256; i++) sumAll += i * histogram[i];

        long weightBackground = 0, sumBackground = 0;
        double bestSpread = -1;
        var best = 128;
        for (var t = 0; t < 256; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0) continue;
            var weightForeground = total - weightBackground;
            if (weightForeground == 0) break;
            sumBackground += t * histogram[t];
            var meanB = (double)sumBackground / weightBackground;
            var meanF = (double)(sumAll - sumBackground) / weightForeground;
            var spread = (double)weightBackground * weightForeground * (meanB - meanF) * (meanB - meanF);
            if (spread > bestSpread) { bestSpread = spread; best = t + 1; }
        }
        return best;
    }
}
