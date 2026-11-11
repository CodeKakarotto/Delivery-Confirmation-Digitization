namespace Confirmations.Api.Ocr;

/// <summary>A field's text as read (null if the cell was empty) and confidence from 0 to 1.</summary>
public sealed record FieldReading(string? Text, float? Confidence)
{
    public static readonly FieldReading Empty = new(null, null);
}

/// <summary>A rectangle on the scanned picture, in pixels.</summary>
public sealed record PixelBox(int Left, int Top, int Width, int Height);

public sealed record TicketReading(
    bool LayoutFound,
    FieldReading Zone,
    FieldReading OrderNumber,
    FieldReading DeliverTo,
    FieldReading TotalProducts,
    FieldReading DeliveryInstructions,
    PixelBox? SignatureArea)
{
    public static readonly TicketReading NotFound = new(false,
        FieldReading.Empty, FieldReading.Empty, FieldReading.Empty, FieldReading.Empty, FieldReading.Empty, null);
}

/// <summary>
/// Finds the five fields on the agreed ticket layout: a heading row
/// ZONE | ORDER ID | DELIVER TO | TOTAL PRODUCTS | DELIVERY INSTRUCTIONS, with
/// each value underneath its heading. Words are placed in a column by where
/// they sit on the page. The instructions stop at "Received by:"; the space
/// below that label is where the signature goes.
/// </summary>
public static class TicketFieldExtractor
{
    private enum Column { Zone, OrderNumber, DeliverTo, TotalProducts, Instructions }

    public static TicketReading Extract(IReadOnlyList<OcrWord> words)
    {
        if (words.Count == 0)
            return TicketReading.NotFound;

        // 1. Find the column headings (the top-most match of each heading word).
        var anchors = FindHeadingAnchors(words);
        if (anchors.Count < 3)
            return TicketReading.NotFound;

        // 2. Measure any tilt from the heading row and straighten all positions.
        var slope = TiltSlope(anchors.Values);
        double Straight(OcrWord w) => w.Top - slope * (w.Left - anchors.Values.Min(a => a.Left));

        var rowHeight = Median(anchors.Values.Select(a => (double)a.Height));
        var headingTop = Median(anchors.Values.Select(Straight));
        bool InHeadingRow(OcrWord w) => Math.Abs(Straight(w) - headingTop) <= rowHeight * 1.5;

        var headingBottom = words.Where(InHeadingRow).Max(w => Straight(w) + w.Height);

        // 3. Each column runs from its heading's left edge to the next heading.
        var starts = anchors.OrderBy(a => a.Value.Left).Select(a => (Column: a.Key, Left: (double)a.Value.Left)).ToList();
        Column? ColumnOf(OcrWord w)
        {
            Column? found = null;
            foreach (var (column, left) in starts)
                if (w.CenterX >= left - rowHeight) found = column;
            return found;
        }

        var values = words
            .Where(w => !InHeadingRow(w) && Straight(w) >= headingBottom - rowHeight * 0.25)
            .GroupBy(ColumnOf)
            .Where(g => g.Key is not null)
            .ToDictionary(g => g.Key!.Value, g => g.ToList());

        List<OcrWord> In(Column c) => values.TryGetValue(c, out var list) ? list : [];

        // 4. Instructions end at "Received by:"; below it is the signature area.
        var instructions = In(Column.Instructions);
        PixelBox? signatureArea = null;
        var received = instructions.Where(w => Letters(w.Text).StartsWith("RECEIVED")).OrderBy(Straight).FirstOrDefault();
        if (received is not null)
        {
            var labelLine = Straight(received);
            instructions = instructions.Where(w => Straight(w) < labelLine - rowHeight * 0.5).ToList();
            var h = received.Height;
            signatureArea = new PixelBox(
                Left: received.Left,
                Top: received.Bottom + (int)(h * 0.2),
                Width: h * 20,
                Height: h * 4);
        }

        return new TicketReading(
            LayoutFound: true,
            Zone: Join(In(Column.Zone), Straight, rowHeight, " "),
            OrderNumber: Join(In(Column.OrderNumber), Straight, rowHeight, " "),
            DeliverTo: Join(In(Column.DeliverTo), Straight, rowHeight, ", "),
            TotalProducts: Join(In(Column.TotalProducts), Straight, rowHeight, " "),
            DeliveryInstructions: Join(instructions, Straight, rowHeight, " "),
            SignatureArea: signatureArea);
    }

    private static Dictionary<Column, OcrWord> FindHeadingAnchors(IReadOnlyList<OcrWord> words)
    {
        var byTop = words.OrderBy(w => w.Top).ThenBy(w => w.Left).ToList();
        OcrWord? First(params string[] names) => byTop.FirstOrDefault(w => names.Contains(Letters(w.Text)));

        var anchors = new Dictionary<Column, OcrWord>();
        void Add(Column c, OcrWord? w) { if (w is not null) anchors[c] = w; }

        Add(Column.Zone, First("ZONE"));
        Add(Column.OrderNumber, First("ORDER"));
        Add(Column.DeliverTo, First("DELIVER"));
        Add(Column.TotalProducts, First("TOTAL") ?? First("PRODUCTS"));
        Add(Column.Instructions, First("DELIVERY") ?? First("INSTRUCTIONS"));

        // Keep only headings that are on the same row as most of the others.
        if (anchors.Count >= 3)
        {
            var height = Median(anchors.Values.Select(a => (double)a.Height));
            var slope = TiltSlope(anchors.Values);
            var x0 = anchors.Values.Min(a => a.Left);
            var rowTop = Median(anchors.Values.Select(a => a.Top - slope * (a.Left - x0)));
            foreach (var (column, word) in anchors.ToList())
                if (Math.Abs(word.Top - slope * (word.Left - x0) - rowTop) > height * 2)
                    anchors.Remove(column);
        }
        return anchors;
    }

    /// <summary>How much the heading row slopes (pixels down per pixel across). 0 = straight.</summary>
    private static double TiltSlope(IEnumerable<OcrWord> headings)
    {
        var points = headings.Select(h => (X: (double)h.Left, Y: (double)h.Top)).ToList();
        if (points.Count < 2) return 0;
        var meanX = points.Average(p => p.X);
        var meanY = points.Average(p => p.Y);
        var spread = points.Sum(p => (p.X - meanX) * (p.X - meanX));
        if (spread == 0) return 0;
        var slope = points.Sum(p => (p.X - meanX) * (p.Y - meanY)) / spread;
        return Math.Abs(slope) > 0.15 ? 0 : slope; // more than ~8°: not a believable tilt
    }

    private static FieldReading Join(List<OcrWord> words, Func<OcrWord, double> top, double rowHeight, string lineSeparator)
    {
        if (words.Count == 0)
            return FieldReading.Empty;

        // Group into lines (words at about the same height), then read left to right.
        var lines = new List<List<OcrWord>>();
        foreach (var word in words.OrderBy(top))
        {
            var line = lines.LastOrDefault();
            if (line is not null && Math.Abs(top(word) - top(line[0])) <= rowHeight * 0.5)
                line.Add(word);
            else
                lines.Add([word]);
        }

        var text = string.Join(lineSeparator, lines.Select(l => string.Join(' ', l.OrderBy(w => w.Left).Select(w => w.Text))));
        var confidence = (float)Math.Round(words.Average(w => w.Confidence) / 100.0, 4);
        return new FieldReading(text, confidence);
    }

    private static string Letters(string text) =>
        new string(text.Where(char.IsLetter).ToArray()).ToUpperInvariant();

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
