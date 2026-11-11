using System.Drawing;
using System.Runtime.Versioning;
using Confirmations.Api.Ocr;

namespace Confirmations.Api.Tests.Ocr;

/// <summary>Step 5: cleaning a picture before OCR (black-and-white, ruled lines erased).</summary>
[SupportedOSPlatform("windows")]
public sealed class ImageCleanerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mf-cleaner-tests", Guid.NewGuid().ToString("N"));

    public ImageCleanerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string DrawSheet()
    {
        using var bmp = new Bitmap(800, 300);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var border = new Pen(Color.Black, 3);
            g.DrawLine(border, 20, 50, 780, 50);    // long horizontal ruling
            g.DrawLine(border, 400, 20, 400, 280);  // long vertical ruling
            using var font = new Font("Arial", 28);
            g.DrawString("MF-10003", font, Brushes.Black, 60, 120);
        }
        var path = Path.Combine(_folder, "sheet.png");
        bmp.Save(path);
        return path;
    }

    private static bool IsDark(Bitmap b, int x, int y) => b.GetPixel(x, y).GetBrightness() < 0.5f;

    [Fact]
    public void ErasesLongLines_KeepsTheWriting()
    {
        var cleaned = new TableLineCleaner().Clean(DrawSheet(), _folder);

        Assert.NotNull(cleaned);
        using var b = new Bitmap(cleaned!);
        Assert.False(IsDark(b, 700, 50));  // horizontal border gone
        Assert.False(IsDark(b, 400, 250)); // vertical border gone
        var textInk = 0;
        for (var x = 60; x < 330; x++)
            for (var y = 120; y < 170; y++)
                if (IsDark(b, x, y)) textInk++;
        Assert.True(textInk > 300, $"text mostly erased ({textInk} dark pixels left)");
    }

    [Fact]
    public void CleanedCopy_IsSeparate_AndTheOriginalIsUntouched()
    {
        var original = DrawSheet();
        var before = File.ReadAllBytes(original);

        var cleaned = new TableLineCleaner().Clean(original, _folder);

        Assert.NotEqual(original, cleaned);
        Assert.Equal(before, File.ReadAllBytes(original));
    }

    [Fact]
    public void UnreadableFile_GivesNull()
    {
        var notAPicture = Path.Combine(_folder, "broken.png");
        File.WriteAllText(notAPicture, "not a picture");

        Assert.Null(new TableLineCleaner().Clean(notAPicture, _folder));
    }
}
