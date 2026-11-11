using System.Runtime.Versioning;
using System.Text;
using Confirmations.Api.Ocr;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Confirmations.Api.Tests.Ocr;

/// <summary>Runs only on a Windows computer that has Tesseract installed; otherwise shown as skipped.</summary>
public sealed class TesseractFactAttribute : FactAttribute
{
    public TesseractFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(new OcrOptions().TesseractPath))
            Skip = "Needs Windows with Tesseract installed (see SETUP.md).";
    }
}

/// <summary>
/// Step 5 integration tests: the REAL Tesseract reads drawn FAKE tickets.
/// OCR doesn't have to be perfect; clean tickets must be read correctly and
/// problems must show up as empty fields / no signature, so Step 6 can flag them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RealTesseractTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mf-fake-tickets", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static TicketReader RealReader() =>
        new(new TesseractCliRunner(Options.Create(new OcrOptions())), new SignatureMarkDetector(), new TableLineCleaner());

    private async Task<TicketScanResult> ReadAsync(FakeTicket ticket) =>
        await RealReader().ReadAsync(ticket.SaveTo(_folder), CancellationToken.None);

    private static FakeTicket Ticket(string name) => FakeTicket.TestSet.Single(t => t.Name == name);

    private static string? Norm(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    [TesseractFact]
    public async Task CleanTickets_AllFieldsAndSignatureReadCorrectly()
    {
        foreach (var ticket in FakeTicket.TestSet.Where(t => t.Name.EndsWith("-clean")))
        {
            var result = await ReadAsync(ticket);
            var r = result.Reading;

            Assert.True(r.LayoutFound, ticket.Name);
            Assert.Equal(ticket.Zone, r.Zone.Text);
            Assert.Equal(ticket.OrderNumber, r.OrderNumber.Text);
            Assert.Equal(ticket.ExpectedDeliverTo, r.DeliverTo.Text);
            Assert.Equal(ticket.TotalProducts, r.TotalProducts.Text);
            Assert.Equal(ticket.Instructions, r.DeliveryInstructions.Text);
            Assert.True(result.Signature?.Detected, $"{ticket.Name}: signature not detected");
        }
    }

    [TesseractFact]
    public async Task MissingZone_IsLeftEmpty()
    {
        var r = (await ReadAsync(Ticket("07-missing-zone"))).Reading;

        Assert.True(r.LayoutFound);
        Assert.Null(r.Zone.Text);
        Assert.Equal("MF-10010", r.OrderNumber.Text);
    }

    [TesseractFact]
    public async Task MissingTotalProducts_IsLeftEmpty()
    {
        var r = (await ReadAsync(Ticket("08-missing-total"))).Reading;

        Assert.True(r.LayoutFound);
        Assert.Null(r.TotalProducts.Text);
        Assert.Equal("MF-10011", r.OrderNumber.Text);
    }

    [TesseractFact]
    public async Task BlurryOrPhonePhotoTickets_TableIsStillFound_AndOrderIdRead()
    {
        foreach (var name in new[] { "05-blurry", "10-phone-photo" })
        {
            var ticket = Ticket(name);
            var r = (await ReadAsync(ticket)).Reading;

            Assert.True(r.LayoutFound, $"{name}: table headings not found");
            Assert.Equal(ticket.OrderNumber, r.OrderNumber.Text);
        }
    }

    [TesseractFact]
    public async Task UnsignedTicket_HasNoSignatureMark()
    {
        var result = await ReadAsync(Ticket("09-no-signature"));

        Assert.False(result.Signature?.Detected);
    }

    [TesseractFact]
    public async Task AccuracyReport_AllFakeTickets()
    {
        string[] fields = ["zone", "order_id", "deliver_to", "total_products", "delivery_instructions", "signature"];
        var correct = fields.ToDictionary(f => f, _ => 0);
        var details = new StringBuilder();

        foreach (var ticket in FakeTicket.TestSet)
        {
            var result = await ReadAsync(ticket);
            var r = result.Reading;
            var checks = new Dictionary<string, bool>
            {
                ["zone"] = Norm(r.Zone.Text) == Norm(ticket.Zone),
                ["order_id"] = Norm(r.OrderNumber.Text) == Norm(ticket.OrderNumber),
                ["deliver_to"] = Norm(r.DeliverTo.Text) == Norm(ticket.ExpectedDeliverTo),
                ["total_products"] = Norm(r.TotalProducts.Text) == Norm(ticket.TotalProducts),
                ["delivery_instructions"] = Norm(r.DeliveryInstructions.Text) == Norm(ticket.Instructions),
                ["signature"] = result.Signature?.Detected == ticket.Signed,
            };
            foreach (var (field, ok) in checks) if (ok) correct[field]++;
            var misses = checks.Where(c => !c.Value).Select(c => c.Key).ToList();
            details.AppendLine($"  {ticket.Name,-18} {(misses.Count == 0 ? "all correct" : "wrong: " + string.Join(", ", misses))}");
        }

        output.WriteLine($"OCR accuracy on {FakeTicket.TestSet.Count} fake tickets:");
        foreach (var f in fields) output.WriteLine($"  {f,-22} read correctly {correct[f]}/{FakeTicket.TestSet.Count}");
        output.WriteLine(details.ToString());

        // Not perfection: the order ID is what links a sheet to an order, so most must be right.
        Assert.True(correct["order_id"] >= 8, $"order_id only {correct["order_id"]}/10");
    }
}
