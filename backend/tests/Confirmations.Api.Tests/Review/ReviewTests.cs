using System.Net;
using System.Net.Http.Json;
using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Confirmations.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Confirmations.Api.Tests.Review;

/// <summary>
/// Step 7: a person checks each sheet, corrects it, and approves, rejects,
/// or sends it to a manager. Nothing becomes a confirmation without this.
/// </summary>
public class ReviewTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private sealed record Flag(string Type, string? Field, string Message);
    private sealed record Original(string? Zone, float? ZoneConfidence, string? OrderNumber, string? TotalProducts,
        bool? SignatureMarkDetected, bool OcrFailed);
    private sealed record Detail(
        Guid ConfirmationId, Guid ScanId, string FileName, string Status, string ReviewStatus, Guid Version,
        string? Zone, string? OrderNumber, string? DeliverTo, int? TotalProducts, string? DeliveryInstructions,
        bool? SignaturePresent, string? ExceptionNotes, string? ReviewerNote, string? ReviewedBy,
        Original? Original, List<Flag> Flags);
    private sealed record Waiting(Guid ConfirmationId, Guid ScanId, string? OrderNumber);
    private sealed record Problem(string Message, List<string>? Reasons);

    private async Task<HttpClient> SignedInAsync(string email = "employee@demo.local")
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = TestWebAppFactory.DemoPassword })).EnsureSuccessStatusCode();
        return client;
    }

    private string UserId(string email)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Single(u => u.Email == email).Id;
    }

    /// <summary>A sheet that has been read, waiting for review. Order MF-1000x must exist (20 fake orders are seeded).</summary>
    private Guid AddWaiting(string? orderNumber = "MF-10001", string? zone = "North", int? total = 2,
        bool? signatureMark = true, DateTime? uploadedAt = null, string instructions = "Leave at side door")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var when = uploadedAt ?? DateTime.UtcNow;
        var scan = new ScanDocument
        {
            OriginalFileName = "sheet.png", StoredFileName = $"{Guid.NewGuid():N}.png", ContentType = "image/png",
            SizeBytes = 1, PageCount = 1, Sha256 = Guid.NewGuid().ToString("N"),
            UploadedByUserId = UserId("employee@demo.local"), UploadedAt = when,
        };
        var record = new DeliveryConfirmation
        {
            ScanDocument = scan, Status = ConfirmationStatus.Pending, CreatedAt = when,
            Zone = zone, OrderNumber = orderNumber, DeliverTo = "Test Recipient 1, 101 Example St",
            TotalProducts = total, DeliveryInstructions = instructions,
        };
        db.AddRange(scan, record, new OcrResult
        {
            ScanDocument = scan, ProcessedAt = when, Zone = zone, ZoneConfidence = 0.61f, OrderNumber = orderNumber,
            OrderNumberConfidence = 0.95f, DeliverTo = record.DeliverTo, TotalProducts = total?.ToString(),
            DeliveryInstructions = instructions, SignatureMarkDetected = signatureMark, SignatureConfidence = 0.9f,
        });
        db.SaveChanges();

        // As in the app: problems are checked right after a sheet is read.
        scope.ServiceProvider.GetRequiredService<ConfirmationValidator>().CheckAsync(record.Id, CancellationToken.None).GetAwaiter().GetResult();
        return record.Id;
    }

    private static object Edit(Detail d, Action<Dictionary<string, object?>>? change = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["version"] = d.Version, ["zone"] = d.Zone, ["orderNumber"] = d.OrderNumber, ["deliverTo"] = d.DeliverTo,
            ["totalProducts"] = d.TotalProducts, ["deliveryInstructions"] = d.DeliveryInstructions,
            ["signaturePresent"] = d.SignaturePresent ?? true, ["exceptionNotes"] = d.ExceptionNotes,
        };
        change?.Invoke(body);
        return body;
    }

    private static async Task<Detail> GetAsync(HttpClient c, Guid id) =>
        (await c.GetFromJsonAsync<Detail>($"/api/confirmations/{id}"))!;

    private DeliveryConfirmation Stored(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().DeliveryConfirmations
            .Include(c => c.Flags).AsNoTracking().Single(c => c.Id == id);
    }

    private List<AuditLogEntry> Audit(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLog
            .Where(a => a.EntityId == id.ToString()).OrderBy(a => a.Id).ToList();
    }

    // ---------- Access ----------

    [Theory]
    [InlineData("GET", "/api/confirmations/waiting")]
    [InlineData("GET", "/api/confirmations/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/confirmations/00000000-0000-0000-0000-000000000001/approve")]
    public async Task NotSignedIn_IsRefused(string method, string url)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method == "POST" ? JsonContent.Create(new { }) : null,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRecord_Returns404()
    {
        var c = await SignedInAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/confirmations/{Guid.NewGuid()}")).StatusCode);
    }

    // ---------- Waiting list and details ----------

    [Fact]
    public async Task WaitingList_ShowsPendingSheets_OldestFirst()
    {
        var older = AddWaiting(uploadedAt: DateTime.UtcNow.AddYears(-5));
        var newer = AddWaiting(uploadedAt: DateTime.UtcNow.AddYears(-5).AddMinutes(1));
        var c = await SignedInAsync();

        var list = (await c.GetFromJsonAsync<List<Waiting>>("/api/confirmations/waiting"))!;

        Assert.True(list.FindIndex(w => w.ConfirmationId == older) < list.FindIndex(w => w.ConfirmationId == newer));
    }

    [Fact]
    public async Task Details_ShowCurrentValues_TheOriginalReading_AndProblems()
    {
        var id = AddWaiting(zone: "Nrth");
        var c = await SignedInAsync();

        var d = await GetAsync(c, id);

        Assert.Equal("Pending", d.Status);
        Assert.Equal("Waiting for review", d.ReviewStatus);
        Assert.Equal("Nrth", d.Zone);
        Assert.Equal("Nrth", d.Original!.Zone);
        Assert.Equal(0.61f, d.Original.ZoneConfidence!.Value, 2);
        Assert.True(d.Original.SignatureMarkDetected);
        Assert.Contains(d.Flags, f => f.Type == "LowConfidence" && f.Field == "Zone");
    }

    // ---------- Saving edits ----------

    [Fact]
    public async Task Save_KeepsCorrections_TheOriginalReading_AndAnAuditEntryWithFieldNamesOnly()
    {
        var id = AddWaiting(zone: "Nrth");
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);

        var response = await c.PutAsJsonAsync($"/api/confirmations/{id}", Edit(d, b =>
        {
            b["zone"] = "North";
            b["exceptionNotes"] = "Driver said gate was locked";
            b["signaturePresent"] = null; // not answered yet
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await GetAsync(c, id);
        Assert.Equal("North", saved.Zone);
        Assert.Equal("Nrth", saved.Original!.Zone); // original reading kept
        Assert.Equal("Driver said gate was locked", saved.ExceptionNotes);
        Assert.Equal("Pending", saved.Status);       // saving never decides anything
        Assert.DoesNotContain(saved.Flags, f => f.Type == "LowConfidence" && f.Field == "Zone");

        var edit = Assert.Single(Audit(id), a => a.Action == AuditAction.Edited);
        Assert.Equal(UserId("employee@demo.local"), edit.UserId);
        Assert.Equal("Zone,ExceptionNotes", edit.ChangedFields); // names only, never values
    }

    [Fact]
    public async Task Save_WithAnOldVersion_IsRefused_SoNobodyOverwritesSomeoneElse()
    {
        var id = AddWaiting();
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);
        (await c.PutAsJsonAsync($"/api/confirmations/{id}", Edit(d, b => b["zone"] = "South"))).EnsureSuccessStatusCode();

        var stale = await c.PutAsJsonAsync($"/api/confirmations/{id}", Edit(d, b => b["zone"] = "East"));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<Problem>();
        Assert.Equal("Someone else changed this record. Please reload it to see their changes.", problem!.Message);
        Assert.Equal("South", Stored(id).Zone);
    }

    [Fact]
    public async Task Save_TooLongText_IsRefusedWithAPlainMessage()
    {
        var id = AddWaiting();
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);

        var response = await c.PutAsJsonAsync($"/api/confirmations/{id}", Edit(d, b => b["exceptionNotes"] = new string('x', 501)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Problem>();
        Assert.Contains("Exceptions / notes can be at most 500 characters.", problem!.Reasons!);
    }

    // ---------- Approve ----------

    [Fact]
    public async Task Approve_GoodSheet_BecomesTheConfirmation_ApprovedByTheSignedInUser()
    {
        var id = AddWaiting(orderNumber: "MF-10002");
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);

        var body = (Dictionary<string, object?>)Edit(d);
        body["reviewedByUserId"] = "someone-else"; // must be ignored
        var response = await c.PostAsJsonAsync($"/api/confirmations/{id}/approve", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Stored(id);
        Assert.Equal(ConfirmationStatus.Approved, stored.Status);
        Assert.Equal(UserId("employee@demo.local"), stored.ReviewedByUserId);
        Assert.NotNull(stored.ReviewedAt);
        Assert.NotNull(stored.OrderId);
        Assert.True(stored.SignaturePresent);
        Assert.All(stored.Flags, f => Assert.NotNull(f.ResolvedAt));
        Assert.Contains(Audit(id), a => a.Action == AuditAction.Approved);
        Assert.Equal("Confirmed", (await GetAsync(c, id)).ReviewStatus);
    }

    [Fact]
    public async Task Approve_IsRefused_UntilEverythingIsFilledIn_WithEveryReasonListed()
    {
        var id = AddWaiting(orderNumber: "MF-10003", zone: null, total: null);
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);

        var response = await c.PostAsJsonAsync($"/api/confirmations/{id}/approve", Edit(d, b => b["signaturePresent"] = null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Problem>();
        Assert.Equal("This sheet can't be approved yet.", problem!.Message);
        Assert.Equal([
            "Zone is missing.",
            "Total products is missing.",
            "Please answer \"Signature present\": yes or no.",
        ], problem.Reasons);
        Assert.Equal(ConfirmationStatus.Pending, Stored(id).Status);
    }

    [Fact]
    public async Task Approve_UnknownOrder_IsRefused()
    {
        var id = AddWaiting(orderNumber: "MF-99999");
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);

        var response = await c.PostAsJsonAsync($"/api/confirmations/{id}/approve", Edit(d));

        var problem = await response.Content.ReadFromJsonAsync<Problem>();
        Assert.Contains("Order ID MF-99999 isn't in the order list.", problem!.Reasons!);
    }

    [Fact]
    public async Task Approve_SecondSheetForAnAlreadyApprovedOrder_IsRefused()
    {
        var first = AddWaiting(orderNumber: "MF-10004");
        var second = AddWaiting(orderNumber: "MF-10004");
        var c = await SignedInAsync();
        (await c.PostAsJsonAsync($"/api/confirmations/{first}/approve", Edit(await GetAsync(c, first)))).EnsureSuccessStatusCode();

        var response = await c.PostAsJsonAsync($"/api/confirmations/{second}/approve", Edit(await GetAsync(c, second)));

        var problem = await response.Content.ReadFromJsonAsync<Problem>();
        Assert.Contains("Order MF-10004 already has an approved confirmation.", problem!.Reasons!);
    }

    [Fact]
    public async Task Approve_WithoutSignature_NeedsANoteExplainingWhy()
    {
        var id = AddWaiting(orderNumber: "MF-10005", signatureMark: false);
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);

        var noNote = await c.PostAsJsonAsync($"/api/confirmations/{id}/approve", Edit(d, b => b["signaturePresent"] = false));
        var problem = await noNote.Content.ReadFromJsonAsync<Problem>();
        Assert.Contains("No signature: please add a note in Exceptions / notes (for example, \"Left at door\").", problem!.Reasons!);

        var withNote = await c.PostAsJsonAsync($"/api/confirmations/{id}/approve", Edit(d, b =>
        {
            b["signaturePresent"] = false;
            b["exceptionNotes"] = "Nobody home, left at front door";
        }));
        Assert.Equal(HttpStatusCode.OK, withNote.StatusCode);
    }

    [Fact]
    public async Task Approve_SavesTheCorrectionsMadeAtTheSameTime()
    {
        var id = AddWaiting(orderNumber: "mf-10006 ", zone: "Nrth");
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);

        (await c.PostAsJsonAsync($"/api/confirmations/{id}/approve", Edit(d, b => b["zone"] = "North"))).EnsureSuccessStatusCode();

        var stored = Stored(id);
        Assert.Equal("North", stored.Zone);
        Assert.Equal("MF-10006", stored.OrderNumber); // tidied
        var actions = Audit(id).Select(a => a.Action).ToList();
        Assert.Equal([AuditAction.Edited, AuditAction.Approved], actions);
    }

    [Fact]
    public async Task AlreadyDecidedRecords_CanNotBeChangedHere()
    {
        var id = AddWaiting(orderNumber: "MF-10007");
        var c = await SignedInAsync();
        (await c.PostAsJsonAsync($"/api/confirmations/{id}/approve", Edit(await GetAsync(c, id)))).EnsureSuccessStatusCode();
        var d = await GetAsync(c, id);

        var edit = await c.PutAsJsonAsync($"/api/confirmations/{id}", Edit(d, b => b["zone"] = "South"));
        var reject = await c.PostAsJsonAsync($"/api/confirmations/{id}/reject", new { version = d.Version });

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        var problem = await edit.Content.ReadFromJsonAsync<Problem>();
        Assert.Equal("This sheet has already been reviewed (Confirmed).", problem!.Message);
    }

    // ---------- Reject and send to manager ----------

    [Fact]
    public async Task Reject_RecordsWhoAndWhy()
    {
        var id = AddWaiting();
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);

        var response = await c.PostAsJsonAsync($"/api/confirmations/{id}/reject", new { version = d.Version, reason = "Wrong sheet" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Stored(id);
        Assert.Equal(ConfirmationStatus.Rejected, stored.Status);
        Assert.Equal("Wrong sheet", stored.ReviewerNote);
        Assert.Equal(UserId("employee@demo.local"), stored.ReviewedByUserId);
        Assert.Contains(Audit(id), a => a.Action == AuditAction.Rejected);
        Assert.Equal("Denied", (await GetAsync(c, id)).ReviewStatus);
    }

    [Fact]
    public async Task SendToManager_NeedsANote_AndKeepsTheProblemsOpen()
    {
        var id = AddWaiting(orderNumber: "MF-99998");
        var c = await SignedInAsync();
        var d = await GetAsync(c, id);

        var noNote = await c.PostAsJsonAsync($"/api/confirmations/{id}/send-to-manager", new { version = d.Version, note = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);
        Assert.Contains("Please write a note for the manager.", (await noNote.Content.ReadFromJsonAsync<Problem>())!.Reasons!);

        var ok = await c.PostAsJsonAsync($"/api/confirmations/{id}/send-to-manager", new { version = d.Version, note = "Order not in list, please check" });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var stored = Stored(id);
        Assert.Equal(ConfirmationStatus.Flagged, stored.Status);
        Assert.Equal("Order not in list, please check", stored.ReviewerNote);
        Assert.Contains(stored.Flags, f => f.ResolvedAt == null);
        Assert.Contains(Audit(id), a => a.Action == AuditAction.SentToManager);
        Assert.Equal("Sent to manager", (await GetAsync(c, id)).ReviewStatus);
    }

    [Fact]
    public async Task ReviewedBy_ShowsTheEmailOfWhoDecided()
    {
        var id = AddWaiting(orderNumber: "MF-10008");
        var c = await SignedInAsync("manager@demo.local");
        (await c.PostAsJsonAsync($"/api/confirmations/{id}/approve", Edit(await GetAsync(c, id)))).EnsureSuccessStatusCode();

        Assert.Equal("manager@demo.local", (await GetAsync(c, id)).ReviewedBy);
    }
}
