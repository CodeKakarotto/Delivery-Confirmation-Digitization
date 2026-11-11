using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Confirmations.Api.Ocr;
using Confirmations.Api.Scans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Confirmations.Api.Tests.Ocr;

/// <summary>
/// Step 5: reading a stored scan and saving the results. Tesseract and the
/// signature check are simulated; the database is a real (SQLite) one.
/// </summary>
public sealed class OcrProcessorTests : IDisposable
{
    private readonly SqliteTestDatabase _testDb = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mf-ocr-tests", Guid.NewGuid().ToString("N"));
    private readonly FileSystemScanStorage _storage;

    public OcrProcessorTests()
    {
        _storage = new FileSystemScanStorage(Options.Create(new ScanStorageOptions { Folder = _folder }));
    }

    public void Dispose()
    {
        _testDb.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private async Task<Guid> AddUploadedScanAsync(Action<DeliveryConfirmation>? editRecord = null)
    {
        var stored = await _storage.SaveAsync(TestImages.Png(withMetadata: false), ".png", CancellationToken.None);
        using var db = _testDb.NewContext();
        var scan = new ScanDocument
        {
            OriginalFileName = "sheet.png", StoredFileName = stored, ContentType = "image/png",
            SizeBytes = 100, PageCount = 1, Sha256 = Guid.NewGuid().ToString("N"),
            UploadedByUserId = _testDb.UserId, UploadedAt = DateTime.UtcNow,
        };
        var record = new DeliveryConfirmation { ScanDocument = scan, Status = ConfirmationStatus.Pending, CreatedAt = DateTime.UtcNow };
        editRecord?.Invoke(record);
        db.AddRange(scan, record);
        await db.SaveChangesAsync();
        return scan.Id;
    }

    private async Task ProcessAsync(Guid scanId, ITesseractRunner runner, ISignatureMarkDetector? signatures = null)
    {
        using var db = _testDb.NewContext();
        var reader = new TicketReader(runner, signatures ?? new FakeSignatureDetector(new SignatureCheck(true, 0.9f)));
        var processor = new OcrProcessor(db, _storage, reader, TimeProvider.System, NullLogger<OcrProcessor>.Instance);
        await processor.ProcessAsync(scanId, CancellationToken.None);
    }

    private (OcrResult Ocr, DeliveryConfirmation Record) Load(Guid scanId)
    {
        using var db = _testDb.NewContext();
        return (db.OcrResults.Single(o => o.ScanDocumentId == scanId),
                db.DeliveryConfirmations.Single(c => c.ScanDocumentId == scanId));
    }

    [Fact]
    public async Task SavesWhatWasRead_AndFillsThePendingRecordAsSuggestions()
    {
        var scanId = await AddUploadedScanAsync();
        var before = DateTime.UtcNow;

        await ProcessAsync(scanId, FakeTesseractRunner.Returning(TsvBuilder.StandardTicket()));

        var (ocr, record) = Load(scanId);
        Assert.False(ocr.OcrFailed);
        Assert.Equal("North", ocr.Zone);
        Assert.Equal(0.97f, ocr.ZoneConfidence!.Value, 2);
        Assert.Equal("MF-10003", ocr.OrderNumber);
        Assert.Equal("Test Recipient 3, 103 Example St", ocr.DeliverTo);
        Assert.Equal("4", ocr.TotalProducts);
        Assert.Equal("Leave at side door", ocr.DeliveryInstructions);
        Assert.True(ocr.SignatureMarkDetected);
        Assert.Equal(0.9f, ocr.SignatureConfidence);
        Assert.Contains("MF-10003", ocr.RawText);
        Assert.InRange(ocr.ProcessedAt, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));

        // Suggestions only: still Pending, nobody has approved anything.
        Assert.Equal(ConfirmationStatus.Pending, record.Status);
        Assert.Null(record.ReviewedByUserId);
        Assert.Equal("North", record.Zone);
        Assert.Equal("MF-10003", record.OrderNumber);
        Assert.Equal(4, record.TotalProducts);
        Assert.Equal("Leave at side door", record.DeliveryInstructions);
        Assert.Null(record.SignaturePresent); // the employee confirms this
    }

    [Fact]
    public async Task TesseractFails_RecordIsMarkedAndStillWaitsForManualEntry()
    {
        var scanId = await AddUploadedScanAsync();

        await ProcessAsync(scanId, FakeTesseractRunner.Crashing());

        var (ocr, record) = Load(scanId);
        Assert.True(ocr.OcrFailed);
        Assert.Null(ocr.OrderNumber);
        Assert.Equal(ConfirmationStatus.Pending, record.Status);
        Assert.Null(record.OrderNumber);
    }

    [Fact]
    public async Task SheetWithoutTheTicketLayout_IsMarkedAsFailed()
    {
        var scanId = await AddUploadedScanAsync();
        var groceryList = new TsvBuilder().Line(new("milk", 50, 50), new("eggs", 200, 50));

        await ProcessAsync(scanId, FakeTesseractRunner.Returning(groceryList));

        var (ocr, record) = Load(scanId);
        Assert.True(ocr.OcrFailed);
        Assert.Equal("milk eggs", ocr.RawText); // what was read is still kept
        Assert.Equal(ConfirmationStatus.Pending, record.Status);
    }

    [Fact]
    public async Task TotalProductsThatIsNotANumber_IsKeptAsReadButNotCopied()
    {
        var scanId = await AddUploadedScanAsync();

        await ProcessAsync(scanId, FakeTesseractRunner.Returning(TsvBuilder.StandardTicket(totalProducts: "Z")));

        var (ocr, record) = Load(scanId);
        Assert.Equal("Z", ocr.TotalProducts);
        Assert.Null(record.TotalProducts);
    }

    [Fact]
    public async Task NeverOverwritesWhatAnEmployeeAlreadyTyped()
    {
        var scanId = await AddUploadedScanAsync(r => r.Zone = "South");

        await ProcessAsync(scanId, FakeTesseractRunner.Returning(TsvBuilder.StandardTicket()));

        var (ocr, record) = Load(scanId);
        Assert.Equal("North", ocr.Zone);     // the original reading is kept…
        Assert.Equal("South", record.Zone);  // …but the employee's value wins
        Assert.Equal("MF-10003", record.OrderNumber);
    }

    [Fact]
    public async Task AlreadyReviewedRecords_AreNotChanged()
    {
        var scanId = await AddUploadedScanAsync(r =>
        {
            r.Status = ConfirmationStatus.Approved;
            r.ReviewedByUserId = _testDb.UserId;
            r.ReviewedAt = DateTime.UtcNow;
        });

        await ProcessAsync(scanId, FakeTesseractRunner.Returning(TsvBuilder.StandardTicket()));

        var (_, record) = Load(scanId);
        Assert.Null(record.Zone);
        Assert.Equal(ConfirmationStatus.Approved, record.Status);
    }

    [Fact]
    public async Task ReadingTheSameScanTwice_DoesNotRunOcrAgain()
    {
        var scanId = await AddUploadedScanAsync();
        var runner = FakeTesseractRunner.Returning(TsvBuilder.StandardTicket());

        await ProcessAsync(scanId, runner);
        await ProcessAsync(scanId, runner);

        Assert.Equal(1, runner.Calls);
        using var db = _testDb.NewContext();
        Assert.Single(db.OcrResults.Where(o => o.ScanDocumentId == scanId));
    }

    [Fact]
    public async Task VeryLongReadings_AreShortenedToFitTheDatabase()
    {
        var scanId = await AddUploadedScanAsync();
        var huge = string.Concat(Enumerable.Repeat("blah", 1000)); // 4000 characters

        await ProcessAsync(scanId, FakeTesseractRunner.Returning(TsvBuilder.StandardTicket(instructionsLastWord: huge)));

        var (ocr, record) = Load(scanId);
        Assert.True(ocr.DeliveryInstructions!.Length <= 2000);
        Assert.True(record.DeliveryInstructions!.Length <= 1000);
    }

    [Fact]
    public async Task SignatureCheck_LooksBelowReceivedBy()
    {
        var scanId = await AddUploadedScanAsync();
        var detector = new FakeSignatureDetector(new SignatureCheck(false, 0.8f));

        await ProcessAsync(scanId, FakeTesseractRunner.Returning(TsvBuilder.StandardTicket()), detector);

        Assert.Equal(1584, detector.LastArea!.Left);
        var (ocr, _) = Load(scanId);
        Assert.False(ocr.SignatureMarkDetected);
    }
}
