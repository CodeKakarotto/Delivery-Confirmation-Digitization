using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace Confirmations.Api.Tests.Ocr;

/// <summary>
/// Draws a FAKE Michael's Flowers delivery ticket (made-up data only) in the
/// agreed layout: one row ZONE | ORDER ID | DELIVER TO | TOTAL PRODUCTS |
/// DELIVERY INSTRUCTIONS, with "Received by:" and a signature line in the
/// instructions cell. Can also make it blurry, tilted, or low quality.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed record FakeTicket(
    string Name,
    string? Zone,
    string? OrderNumber,
    string DeliverToName,
    string DeliverToAddress,
    string? TotalProducts,
    string Instructions,
    bool Signed = true,
    bool Blurry = false,
    float TiltDegrees = 0,
    bool LowQualityPhoto = false)
{
    public string ExpectedDeliverTo => $"{DeliverToName}, {DeliverToAddress}";

    private static readonly int[] Columns = [60, 340, 700, 1240, 1560, 2140];
    private static readonly string[] Headings = ["ZONE", "ORDER ID", "DELIVER TO", "TOTAL PRODUCTS", "DELIVERY INSTRUCTIONS"];

    public string SaveTo(string folder)
    {
        Directory.CreateDirectory(folder);
        using var ticket = Draw();
        using var finished = PostProcess(ticket);
        var path = Path.Combine(folder, Name + (LowQualityPhoto ? ".jpg" : ".png"));
        if (LowQualityPhoto)
        {
            var jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
            using var quality = new EncoderParameters(1);
            quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 45L);
            finished.Save(path, jpeg, quality);
        }
        else
        {
            finished.Save(path, ImageFormat.Png);
        }
        return path;
    }

    private Bitmap Draw()
    {
        var bmp = new Bitmap(2200, 700);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var border = new Pen(Color.Black, 3);
        using var headingFont = new Font("Arial", 26, FontStyle.Bold);
        using var valueFont = new Font("Arial", 28);

        const int top = 80, headingBottom = 160, bottom = 620;
        g.DrawRectangle(border, Columns[0], top, Columns[5] - Columns[0], bottom - top);
        g.DrawLine(border, Columns[0], headingBottom, Columns[5], headingBottom);
        for (var i = 1; i < 5; i++) g.DrawLine(border, Columns[i], top, Columns[i], bottom);
        for (var i = 0; i < 5; i++) g.DrawString(Headings[i], headingFont, Brushes.Black, Columns[i] + 15, top + 20);

        void Value(int column, string? text)
        {
            if (!string.IsNullOrEmpty(text))
                g.DrawString(text, valueFont, Brushes.Black, Columns[column] + 15, headingBottom + 30);
        }

        Value(0, Zone);
        Value(1, OrderNumber);
        Value(2, $"{DeliverToName}\n{DeliverToAddress}");
        Value(3, TotalProducts);
        Value(4, $"{Instructions}\n\nReceived by:");

        // Signature line, about 3 label-heights below "Received by:".
        using var line = new Pen(Color.Black, 2);
        g.DrawLine(line, Columns[4] + 15, 470, Columns[5] - 20, 470);

        if (Signed)
        {
            using var ink = new Pen(Color.FromArgb(20, 20, 110), 4);
            g.DrawBezier(ink, 1610, 455, 1660, 380, 1710, 480, 1770, 410);
            g.DrawBezier(ink, 1770, 410, 1810, 370, 1860, 470, 1920, 420);
            g.DrawBezier(ink, 1920, 420, 1950, 400, 1990, 450, 2030, 430);
        }
        return bmp;
    }

    private Bitmap PostProcess(Bitmap ticket)
    {
        var result = new Bitmap(ticket);
        if (TiltDegrees != 0)
        {
            var tilted = new Bitmap(ticket.Width, ticket.Height);
            using var g = Graphics.FromImage(tilted);
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TranslateTransform(ticket.Width / 2f, ticket.Height / 2f);
            g.RotateTransform(TiltDegrees);
            g.TranslateTransform(-ticket.Width / 2f, -ticket.Height / 2f);
            g.DrawImage(ticket, 0, 0);
            result.Dispose();
            result = tilted;
        }
        if (Blurry || LowQualityPhoto)
        {
            // Shrink then enlarge again: loses detail like an out-of-focus or cheap photo.
            var factor = Blurry ? 0.33 : 0.6;
            using var small = new Bitmap(result, (int)(result.Width * factor), (int)(result.Height * factor));
            var blurred = new Bitmap(result.Width, result.Height);
            using var g = Graphics.FromImage(blurred);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(small, 0, 0, result.Width, result.Height);
            result.Dispose();
            result = blurred;
        }
        return result;
    }

    /// <summary>The fake tickets used for accuracy testing (all data made up).</summary>
    public static IReadOnlyList<FakeTicket> TestSet { get; } =
    [
        new("01-clean", "North", "MF-10004", "Test Recipient 4", "104 Example St", "1", "Ring doorbell"),
        new("02-clean", "South", "MF-10005", "Test Recipient 5", "105 Example St", "2", "Leave at side door"),
        new("03-clean", "East", "MF-10006", "Test Recipient 6", "106 Example St", "3", "Call on arrival"),
        new("04-clean", "Downtown", "MF-10007", "Test Recipient 7", "107 Example St", "4", "Front desk"),
        new("05-blurry", "West", "MF-10008", "Test Recipient 8", "108 Example St", "1", "Back porch", Blurry: true),
        new("06-tilted", "North", "MF-10009", "Test Recipient 9", "109 Example St", "2", "Leave with neighbor", TiltDegrees: 1.5f),
        new("07-missing-zone", null, "MF-10010", "Test Recipient 10", "110 Example St", "3", "Gate code 1234"),
        new("08-missing-total", "South", "MF-10011", "Test Recipient 11", "111 Example St", null, "Ring twice"),
        new("09-no-signature", "East", "MF-10012", "Test Recipient 12", "112 Example St", "2", "Nobody home", Signed: false),
        new("10-phone-photo", "West", "MF-10013", "Test Recipient 13", "113 Example St", "5", "Leave at door", LowQualityPhoto: true),
    ];
}
