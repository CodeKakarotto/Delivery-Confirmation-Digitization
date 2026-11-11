using System.Globalization;

namespace Confirmations.Api.Ocr;

/// <summary>One word Tesseract found, with where it is on the page and how sure it is (0–100).</summary>
public sealed record OcrWord(string Text, float Confidence, int Left, int Top, int Width, int Height, int Block, int Paragraph, int Line)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public double CenterX => Left + Width / 2.0;
}

/// <summary>Reads Tesseract's "tsv" output format (one row per word, tab-separated).</summary>
public static class TesseractTsv
{
    public static List<OcrWord> Parse(string tsv)
    {
        var words = new List<OcrWord>();
        foreach (var line in tsv.Split('\n'))
        {
            var cols = line.TrimEnd('\r').Split('\t');
            // level page block par line word left top width height conf text
            if (cols.Length < 12 || cols[0] != "5") continue;

            var text = cols[11].Trim();
            if (text.Length == 0) continue;
            if (!float.TryParse(cols[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var conf) || conf < 0) continue;

            if (int.TryParse(cols[2], out var block) && int.TryParse(cols[3], out var par) && int.TryParse(cols[4], out var ln)
                && int.TryParse(cols[6], out var left) && int.TryParse(cols[7], out var top)
                && int.TryParse(cols[8], out var width) && int.TryParse(cols[9], out var height))
            {
                words.Add(new OcrWord(text, conf, left, top, width, height, block, par, ln));
            }
        }
        return words;
    }

    /// <summary>All the text that was read, line by line, in Tesseract's reading order.</summary>
    public static string ToRawText(IEnumerable<OcrWord> words) =>
        string.Join('\n', words
            .GroupBy(w => (w.Block, w.Paragraph, w.Line))
            .Select(line => string.Join(' ', line.Select(w => w.Text))));
}
