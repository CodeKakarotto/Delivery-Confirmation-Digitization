using Confirmations.Api.Ocr;

namespace Confirmations.Api.Tests.Ocr;

/// <summary>
/// Step 5: turning Tesseract's words-with-positions into the five ticket
/// fields. Uses simulated Tesseract output only (no real OCR).
/// </summary>
public class TicketFieldExtractorTests
{
    private static TicketReading Read(TsvBuilder ticket) =>
        TicketFieldExtractor.Extract(TesseractTsv.Parse(ticket.Build()));

    [Fact]
    public void ReadsAllFiveFields_FromTheirColumns()
    {
        var reading = Read(TsvBuilder.StandardTicket());

        Assert.True(reading.LayoutFound);
        Assert.Equal("North", reading.Zone.Text);
        Assert.Equal("MF-10003", reading.OrderNumber.Text);
        Assert.Equal("Test Recipient 3, 103 Example St", reading.DeliverTo.Text);
        Assert.Equal("4", reading.TotalProducts.Text);
        Assert.Equal("Leave at side door", reading.DeliveryInstructions.Text);
    }

    [Fact]
    public void ConfidenceIsTheAverageOfTheFieldsWords_From0To1()
    {
        var reading = Read(TsvBuilder.StandardTicket());

        Assert.Equal(0.97f, reading.Zone.Confidence!.Value, 2);
        Assert.Equal(0.91f, reading.OrderNumber.Confidence!.Value, 2);
        // Test 96, Recipient 96, 3 97, 103 97, Example 97, St 97
        Assert.Equal(0.9667f, reading.DeliverTo.Confidence!.Value, 3);
    }

    [Fact]
    public void Instructions_StopAtReceivedBy_AndIgnoreSignatureScribbles()
    {
        var reading = Read(TsvBuilder.StandardTicket());

        Assert.DoesNotContain("Received", reading.DeliveryInstructions.Text);
        Assert.DoesNotContain("eas", reading.DeliveryInstructions.Text);
    }

    [Fact]
    public void SignatureArea_IsTheSpaceBelowReceivedBy()
    {
        var area = Read(TsvBuilder.StandardTicket()).SignatureArea;

        Assert.NotNull(area);
        Assert.Equal(1584, area!.Left);            // starts where "Received" starts
        Assert.True(area.Top > 283 + 30 - 5);      // below the label
        Assert.True(area.Top + area.Height > 405); // reaches the signature line area
        Assert.True(area.Width > 300);
    }

    [Fact]
    public void EmptyCell_GivesNoValueAndNoConfidence()
    {
        var reading = Read(TsvBuilder.StandardTicket(zone: null));

        Assert.True(reading.LayoutFound);
        Assert.Null(reading.Zone.Text);
        Assert.Null(reading.Zone.Confidence);
        Assert.Equal("MF-10003", reading.OrderNumber.Text);
    }

    [Fact]
    public void NoTicketHeadings_LayoutNotFound()
    {
        var notATicket = new TsvBuilder()
            .Line(new("Grocery", 50, 50), new("list", 200, 50))
            .Line(new("milk", 50, 120), new("eggs", 200, 120));

        var reading = Read(notATicket);

        Assert.False(reading.LayoutFound);
        Assert.Null(reading.OrderNumber.Text);
        Assert.Null(reading.SignatureArea);
    }

    [Fact]
    public void SlightlyTiltedScan_StillReadsTheRightColumns()
    {
        // 3% tilt: the right side of the sheet sits ~60 px lower than the left.
        var tilted = TsvBuilder.StandardTicket(tilt: (left, top) => top + (int)(left * 0.03));

        var reading = Read(tilted);

        Assert.True(reading.LayoutFound);
        Assert.Equal("North", reading.Zone.Text);
        Assert.Equal("MF-10003", reading.OrderNumber.Text);
        Assert.Equal("4", reading.TotalProducts.Text);
        Assert.Equal("Leave at side door", reading.DeliveryInstructions.Text);
    }

    [Fact]
    public void TextThatLooksLikeHtml_IsKeptExactlyAsRead()
    {
        // Never "cleaned" or interpreted here; the screens show it as plain text.
        var reading = Read(TsvBuilder.StandardTicket(instructionsLastWord: "<script>alert(1)</script>"));

        Assert.Equal("Leave at side <script>alert(1)</script>", reading.DeliveryInstructions.Text);
    }
}

public class TesseractTsvTests
{
    [Fact]
    public void Parse_KeepsOnlyRealWords()
    {
        var tsv = TsvBuilder.Header + "\n" +
            "1\t1\t0\t0\t0\t0\t0\t0\t2200\t700\t-1\t\n" +
            "5\t1\t1\t1\t1\t1\t10\t20\t60\t30\t95.5\tHello\n" +
            "5\t1\t1\t1\t1\t2\t80\t20\t60\t30\t-1\t \n" +
            "5\t1\t1\t1\t1\t3\t150\t20\t60\t30\t88\tthere\n";

        var words = TesseractTsv.Parse(tsv);

        Assert.Equal(["Hello", "there"], words.Select(w => w.Text));
        Assert.Equal(95.5f, words[0].Confidence);
        Assert.Equal(10, words[0].Left);
        Assert.Equal(30, words[0].Height);
    }

    [Fact]
    public void Parse_EmptyOrGarbageOutput_GivesNoWords()
    {
        Assert.Empty(TesseractTsv.Parse(""));
        Assert.Empty(TesseractTsv.Parse("not tsv at all"));
    }

    [Fact]
    public void RawText_KeepsLinesInReadingOrder()
    {
        var words = TesseractTsv.Parse(new TsvBuilder()
            .Line(new("ZONE", 80, 100), new("ORDER", 360, 100))
            .Line(new("North", 80, 200), new("MF-10003", 360, 200))
            .Build());

        Assert.Equal("ZONE ORDER\nNorth MF-10003", TesseractTsv.ToRawText(words));
    }
}
