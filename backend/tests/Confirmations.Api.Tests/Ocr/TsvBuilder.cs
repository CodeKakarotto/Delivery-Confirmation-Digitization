using System.Globalization;
using System.Text;

namespace Confirmations.Api.Tests.Ocr;

/// <summary>
/// Builds fake Tesseract "TSV" output (one row per word, with position and
/// confidence) so tests never need the real Tesseract program.
/// </summary>
public sealed class TsvBuilder
{
    public sealed record W(string Text, int Left, int Top, float Conf = 96, int Height = 30)
    {
        public int Width => Math.Max(12, Text.Length * 20);
    }

    private readonly List<(int Line, W Word)> _words = [];
    private int _line;

    public TsvBuilder Line(params W[] words)
    {
        _line++;
        foreach (var w in words) _words.Add((_line, w));
        return this;
    }

    public static readonly string Header =
        "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext";

    public string Build()
    {
        var sb = new StringBuilder(Header).Append('\n');
        // Page / block rows (level 1-4) carry no words and conf -1, like the real output.
        sb.Append("1\t1\t0\t0\t0\t0\t0\t0\t2200\t700\t-1\t\n");
        var wordNum = 0;
        foreach (var (line, w) in _words)
        {
            wordNum++;
            sb.Append(string.Join('\t',
                "5", "1", line.ToString(CultureInfo.InvariantCulture), "1", "1", wordNum.ToString(CultureInfo.InvariantCulture),
                w.Left.ToString(CultureInfo.InvariantCulture), w.Top.ToString(CultureInfo.InvariantCulture),
                w.Width.ToString(CultureInfo.InvariantCulture), w.Height.ToString(CultureInfo.InvariantCulture),
                w.Conf.ToString("0.000000", CultureInfo.InvariantCulture), w.Text)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// A ticket exactly as the real Tesseract read our fake layout
    /// (positions copied from a real run), with "eas" = signature scribble.
    /// </summary>
    public static TsvBuilder StandardTicket(
        string? zone = "North",
        string totalProducts = "4",
        string instructionsLastWord = "door",
        Func<int, int, int>? tilt = null)
    {
        tilt ??= (_, top) => top;
        W At(string text, int left, int top, float conf = 96) => new(text, left, tilt(left, top), conf);

        var b = new TsvBuilder()
            .Line(At("ZONE", 81, 107, 97), At("ORDER", 363, 107), At("ID", 497, 107, 91),
                  At("DELIVER", 724, 107, 97), At("TO", 882, 107, 97), At("TOTAL", 1263, 107),
                  At("PRODUCTS", 1386, 82, 85), At("DELIVERY", 1584, 107, 97), At("INSTRUCTIONS", 1765, 107, 97));

        var valueRow = new List<W>();
        if (zone is not null) valueRow.Add(At(zone, 84, 197, 97));
        valueRow.AddRange([
            At("MF-10003", 364, 197, 91),
            At("Test", 722, 197, 96), At("Recipient", 801, 197, 96), At("3", 962, 197, 97),
            At(totalProducts, 1262, 197, 96),
            At("Leave", 1584, 197, 96), At("at", 1691, 198, 96), At("side", 1730, 197, 97), At(instructionsLastWord, 1806, 197, 97),
        ]);
        b.Line([.. valueRow]);
        b.Line(At("103", 725, 240, 97), At("Example", 797, 240, 97), At("St", 947, 240, 97));
        b.Line(At("Received", 1584, 283, 96), At("by:", 1745, 283, 96));
        b.Line(At("eas", 1799, 405, 36));
        return b;
    }
}
