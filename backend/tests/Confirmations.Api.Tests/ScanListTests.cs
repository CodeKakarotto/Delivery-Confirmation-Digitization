using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Confirmations.Api.Tests;

/// <summary>
/// Lists of saved scans: the "Orders" box under the upload page (most recent)
/// and the "All scans" page (by week and day). Employees and Managers only.
/// </summary>
public class ScanListTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private sealed record ScanSummary(
        Guid ScanId, string FileName, DateTime UploadedAt, string Status,
        string? Zone, string? OrderNumber, string? DeliverTo, int? TotalProducts,
        string? DeliveryInstructions, int OpenFlags,
        string ReviewStatus, List<string> Exceptions);

    private async Task<HttpClient> SignedInAsync()
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = "employee@demo.local", password = TestWebAppFactory.DemoPassword })).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Adds a saved scan directly, as if uploaded at a given time.</summary>
    private Guid AddScan(string fileName, DateTime uploadedAtUtc, bool? readOk = true,
        string? orderNumber = "MF-10001", bool hidden = false, int openFlags = 0,
        ConfirmationStatus status = ConfirmationStatus.Pending, string instructions = "Leave at side door",
        FlagType? flag = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scan = new ScanDocument
        {
            OriginalFileName = fileName, StoredFileName = $"{Guid.NewGuid():N}.png", ContentType = "image/png",
            SizeBytes = 1, PageCount = 1, Sha256 = Guid.NewGuid().ToString("N"),
            UploadedByUserId = db.Users.Single(u => u.Email == "employee@demo.local").Id,
            UploadedAt = uploadedAtUtc, IsDeleted = hidden,
        };
        var record = new DeliveryConfirmation
        {
            ScanDocument = scan, Status = status, CreatedAt = uploadedAtUtc,
            Zone = "North", OrderNumber = orderNumber, DeliverTo = "Test Recipient 1, 101 Example St",
            TotalProducts = 2, DeliveryInstructions = instructions,
        };
        if (flag is not null)
            db.Add(new ValidationFlag { DeliveryConfirmation = record, Type = flag.Value, Message = "x", CreatedAt = uploadedAtUtc });
        db.AddRange(scan, record);
        if (readOk is not null)
            db.Add(new OcrResult { ScanDocument = scan, OcrFailed = !readOk.Value, ProcessedAt = uploadedAtUtc });
        for (var i = 0; i < openFlags; i++)
            db.Add(new ValidationFlag { DeliveryConfirmation = record, Type = FlagType.MissingField, Message = "x", CreatedAt = uploadedAtUtc });
        db.SaveChanges();
        return scan.Id;
    }

    [Theory]
    [InlineData("/api/scans/recent")]
    [InlineData("/api/scans/history")]
    public async Task Lists_RequireSigningIn(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Recent_ShowsNewestFirst_WithTheTicketFields()
    {
        var now = DateTime.UtcNow;
        AddScan("recent-older.png", now.AddMinutes(-10));
        var newest = AddScan("recent-newest.png", now.AddMinutes(-1), openFlags: 2);
        var client = await SignedInAsync();

        var list = (await client.GetFromJsonAsync<List<ScanSummary>>("/api/scans/recent?take=50"))!;

        // Other tests share this database, so compare only this test's two scans.
        var mine = list.Where(s => s.FileName.StartsWith("recent-")).ToList();
        Assert.Equal(["recent-newest.png", "recent-older.png"], mine.Select(s => s.FileName));
        var first = mine[0];
        Assert.Equal(newest, first.ScanId);
        Assert.Equal("Read", first.Status);
        Assert.Equal("North", first.Zone);
        Assert.Equal("MF-10001", first.OrderNumber);
        Assert.Equal("Test Recipient 1, 101 Example St", first.DeliverTo);
        Assert.Equal(2, first.TotalProducts);
        Assert.Equal("Leave at side door", first.DeliveryInstructions);
        Assert.Equal(2, first.OpenFlags);
    }

    [Fact]
    public async Task Recent_ShowsWhetherEachSheetWasReadFailedOrIsStillBeingRead()
    {
        var now = DateTime.UtcNow;
        var ok = AddScan("status-ok.png", now, readOk: true);
        var failed = AddScan("status-failed.png", now, readOk: false);
        var waiting = AddScan("status-waiting.png", now, readOk: null);
        var client = await SignedInAsync();

        var list = (await client.GetFromJsonAsync<List<ScanSummary>>("/api/scans/recent?take=50"))!;

        Assert.Equal("Read", list.Single(s => s.ScanId == ok).Status);
        Assert.Equal("Failed", list.Single(s => s.ScanId == failed).Status);
        Assert.Equal("Reading", list.Single(s => s.ScanId == waiting).Status);
    }

    [Theory]
    [InlineData(ConfirmationStatus.Pending, "Waiting for review")]
    [InlineData(ConfirmationStatus.Approved, "Confirmed")]
    [InlineData(ConfirmationStatus.Rejected, "Denied")]
    [InlineData(ConfirmationStatus.Flagged, "Sent to manager")]
    [InlineData(ConfirmationStatus.Voided, "Voided")]
    public async Task EachScan_SaysWhetherItIsConfirmedOrDenied(ConfirmationStatus status, string expected)
    {
        var id = AddScan($"review-{status}.png", DateTime.UtcNow, status: status);
        var client = await SignedInAsync();

        var list = (await client.GetFromJsonAsync<List<ScanSummary>>("/api/scans/recent?take=50"))!;

        Assert.Equal(expected, list.Single(s => s.ScanId == id).ReviewStatus);
    }

    [Fact]
    public async Task Exceptions_ListDeliveryExceptionsAndProblemsFound()
    {
        var id = AddScan("exceptions.png", DateTime.UtcNow,
            instructions: "Nobody home, left with neighbor", flag: FlagType.MissingSignature);
        var plain = AddScan("no-exceptions.png", DateTime.UtcNow);
        var client = await SignedInAsync();

        var list = (await client.GetFromJsonAsync<List<ScanSummary>>("/api/scans/recent?take=50"))!;

        Assert.Equal(["Left with neighbor", "Nobody home", "No signature"], list.Single(s => s.ScanId == id).Exceptions);
        Assert.Empty(list.Single(s => s.ScanId == plain).Exceptions);
    }

    [Fact]
    public async Task Recent_ReturnsAtMost50()
    {
        for (var i = 0; i < 55; i++) AddScan($"many-{i}.png", DateTime.UtcNow.AddSeconds(-i));
        var client = await SignedInAsync();

        var list = await client.GetFromJsonAsync<List<ScanSummary>>("/api/scans/recent?take=1000");

        Assert.Equal(50, list!.Count);
    }

    [Fact]
    public async Task Times_AreSentAsUtc_SoBrowsersShowTheRightLocalTime()
    {
        AddScan("utc-check.png", DateTime.UtcNow);
        var client = await SignedInAsync();

        var json = await client.GetStringAsync("/api/scans/recent?take=50");

        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.EnumerateArray().First(e => e.GetProperty("fileName").GetString() == "utc-check.png");
        Assert.EndsWith("Z", item.GetProperty("uploadedAt").GetString());
    }

    [Fact]
    public async Task History_OnlyIncludesTheRequestedNumberOfDays()
    {
        var now = DateTime.UtcNow;
        var inside = AddScan("history-3-days-ago.png", now.AddDays(-3));
        var outside = AddScan("history-40-days-ago.png", now.AddDays(-40));
        var client = await SignedInAsync();

        var list = await client.GetFromJsonAsync<List<ScanSummary>>("/api/scans/history?days=30");

        Assert.Contains(list!, s => s.ScanId == inside);
        Assert.DoesNotContain(list!, s => s.ScanId == outside);
    }

    [Fact]
    public async Task HiddenScans_AreNeverListed()
    {
        var hidden = AddScan("hidden.png", DateTime.UtcNow, hidden: true);
        var client = await SignedInAsync();

        var recent = await client.GetFromJsonAsync<List<ScanSummary>>("/api/scans/recent?take=50");
        var history = await client.GetFromJsonAsync<List<ScanSummary>>("/api/scans/history");

        Assert.DoesNotContain(recent!, s => s.ScanId == hidden);
        Assert.DoesNotContain(history!, s => s.ScanId == hidden);
    }
}
