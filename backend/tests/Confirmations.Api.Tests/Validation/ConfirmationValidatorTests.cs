using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Confirmations.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Tests.Validation;

/// <summary>
/// Step 6: problems are pointed out as flags for a person to review.
/// Flags never approve, reject, or change a record's status.
/// </summary>
public sealed class ConfirmationValidatorTests : IDisposable
{
    private readonly SqliteTestDatabase _testDb = new();

    public ConfirmationValidatorTests()
    {
        using var db = _testDb.NewContext();
        db.Orders.AddRange(
            new Order { OrderNumber = "MF-10003", Zone = "East", DeliverTo = "Test Recipient 3", TotalProducts = 4, DeliveryDate = new DateOnly(2026, 11, 2) },
            new Order { OrderNumber = "MF-10004", Zone = "West", DeliverTo = "Test Recipient 4", TotalProducts = 1, DeliveryDate = new DateOnly(2026, 11, 2) });
        db.SaveChanges();
    }

    public void Dispose() => _testDb.Dispose();

    /// <summary>A record as the OCR step leaves it: suggestions copied from a good reading.</summary>
    private Guid AddRecord(
        Action<DeliveryConfirmation>? record = null,
        Action<OcrResult>? ocr = null,
        bool withOcr = true,
        ConfirmationStatus status = ConfirmationStatus.Pending)
    {
        using var db = _testDb.NewContext();
        var scan = new ScanDocument
        {
            OriginalFileName = "sheet.png", StoredFileName = $"{Guid.NewGuid():N}.png", ContentType = "image/png",
            SizeBytes = 1, PageCount = 1, Sha256 = Guid.NewGuid().ToString("N"),
            UploadedByUserId = _testDb.UserId, UploadedAt = DateTime.UtcNow,
        };
        var r = new DeliveryConfirmation
        {
            ScanDocument = scan, Status = status, CreatedAt = DateTime.UtcNow,
            Zone = "East", OrderNumber = "MF-10003", DeliverTo = "Test Recipient 3, 103 Example St",
            TotalProducts = 4, DeliveryInstructions = "Leave at side door",
        };
        record?.Invoke(r);
        db.AddRange(scan, r);
        if (withOcr)
        {
            var o = new OcrResult
            {
                ScanDocument = scan, ProcessedAt = DateTime.UtcNow,
                Zone = "East", ZoneConfidence = 0.97f,
                OrderNumber = "MF-10003", OrderNumberConfidence = 0.95f,
                DeliverTo = "Test Recipient 3, 103 Example St", DeliverToConfidence = 0.96f,
                TotalProducts = "4", TotalProductsConfidence = 0.96f,
                DeliveryInstructions = "Leave at side door", DeliveryInstructionsConfidence = 0.96f,
                SignatureMarkDetected = true, SignatureConfidence = 0.9f,
            };
            ocr?.Invoke(o);
            db.Add(o);
        }
        db.SaveChanges();
        return r.Id;
    }

    private async Task CheckAsync(Guid recordId)
    {
        using var db = _testDb.NewContext();
        await new ConfirmationValidator(db, TimeProvider.System).CheckAsync(recordId, CancellationToken.None);
    }

    private List<ValidationFlag> OpenFlags(Guid recordId)
    {
        using var db = _testDb.NewContext();
        return db.ValidationFlags.Where(f => f.DeliveryConfirmationId == recordId && f.ResolvedAt == null).ToList();
    }

    private DeliveryConfirmation Record(Guid id)
    {
        using var db = _testDb.NewContext();
        return db.DeliveryConfirmations.Single(c => c.Id == id);
    }

    [Fact]
    public async Task GoodSheet_HasNoFlags_AndIsLinkedToItsOrder()
    {
        var id = AddRecord();

        await CheckAsync(id);

        Assert.Empty(OpenFlags(id));
        using var db = _testDb.NewContext();
        var record = db.DeliveryConfirmations.Include(c => c.Order).Single(c => c.Id == id);
        Assert.Equal("MF-10003", record.Order!.OrderNumber);
        Assert.Equal(ConfirmationStatus.Pending, record.Status); // flags/checks never decide anything
    }

    [Fact]
    public async Task MissingFields_EachGetAPlainFlag()
    {
        var id = AddRecord(
            r => { r.Zone = null; r.DeliverTo = " "; r.DeliveryInstructions = null; },
            o => { o.Zone = null; o.DeliverTo = null; o.DeliveryInstructions = null; });

        await CheckAsync(id);

        var flags = OpenFlags(id).Where(f => f.Type == FlagType.MissingField).ToList();
        Assert.Equal(["DeliverTo", "DeliveryInstructions", "Zone"], flags.Select(f => f.Field).Order());
        Assert.Contains(flags, f => f.Message == "Zone is missing.");
        Assert.Contains(flags, f => f.Message == "Deliver to is missing.");
    }

    [Fact]
    public async Task UnknownOrderId_IsFlagged_AndNotLinked()
    {
        var id = AddRecord(r => r.OrderNumber = "MF-99999", o => o.OrderNumber = "MF-99999");

        await CheckAsync(id);

        var flag = Assert.Single(OpenFlags(id));
        Assert.Equal(FlagType.OrderNotFound, flag.Type);
        Assert.Equal("Order ID MF-99999 isn't in the order list.", flag.Message);
        Assert.Null(Record(id).OrderId);
    }

    [Fact]
    public async Task OrderId_IsMatchedIgnoringCaseAndSpaces()
    {
        var id = AddRecord(r => r.OrderNumber = "  mf-10004 ");

        await CheckAsync(id);

        Assert.DoesNotContain(OpenFlags(id), f => f.Type == FlagType.OrderNotFound);
        Assert.NotNull(Record(id).OrderId);
    }

    [Fact]
    public async Task OrderThatIsAlreadyApproved_IsFlagged()
    {
        var approved = AddRecord(status: ConfirmationStatus.Approved);
        await CheckAsync(approved); // links it to MF-10003
        using (var db = _testDb.NewContext())
        {
            var order = db.Orders.Single(o => o.OrderNumber == "MF-10003");
            db.DeliveryConfirmations.Single(c => c.Id == approved).OrderId = order.Id;
            db.SaveChanges();
        }
        var second = AddRecord();

        await CheckAsync(second);

        var flag = Assert.Single(OpenFlags(second));
        Assert.Equal(FlagType.OrderAlreadyConfirmed, flag.Type);
        Assert.Equal("Order MF-10003 already has an approved confirmation.", flag.Message);
    }

    [Fact]
    public async Task TwoSheetsWaitingForTheSameOrder_AreBothFlagged()
    {
        var first = AddRecord();
        var second = AddRecord();

        await CheckAsync(first);
        await CheckAsync(second);
        await CheckAsync(first); // checked again now that the second one is linked

        Assert.Equal(FlagType.DuplicateOrder, Assert.Single(OpenFlags(first)).Type);
        var flag = Assert.Single(OpenFlags(second));
        Assert.Equal(FlagType.DuplicateOrder, flag.Type);
        Assert.Equal("Another sheet for order MF-10003 is waiting for review.", flag.Message);
    }

    [Fact]
    public async Task SecondSheetForAnOrder_AlsoFlagsTheFirstOneRightAway()
    {
        var first = AddRecord();
        await CheckAsync(first);
        Assert.Empty(OpenFlags(first));

        var second = AddRecord();
        await CheckAsync(second); // only the new sheet is checked…

        // …but the earlier sheet for the same order is flagged too.
        Assert.Equal(FlagType.DuplicateOrder, Assert.Single(OpenFlags(first)).Type);
        Assert.Equal(FlagType.DuplicateOrder, Assert.Single(OpenFlags(second)).Type);
    }

    [Theory]
    [InlineData(null, "Z","Total products must be a whole number above 0 (the sheet shows \"Z\").")]
    [InlineData(0, "0", "Total products must be a whole number above 0.")]
    [InlineData(-2, "-2", "Total products must be a whole number above 0.")]
    public async Task BadProductCount_IsFlagged(int? value, string asRead, string expectedMessage)
    {
        var id = AddRecord(r => r.TotalProducts = value, o => o.TotalProducts = asRead);

        await CheckAsync(id);

        var flag = Assert.Single(OpenFlags(id));
        Assert.Equal(FlagType.InvalidTotalProducts, flag.Type);
        Assert.Equal(expectedMessage, flag.Message);
    }

    [Fact]
    public async Task EmptyProductCount_IsAMissingField()
    {
        var id = AddRecord(r => r.TotalProducts = null, o => o.TotalProducts = null);

        await CheckAsync(id);

        var flag = Assert.Single(OpenFlags(id));
        Assert.Equal(FlagType.MissingField, flag.Type);
        Assert.Equal("Total products is missing.", flag.Message);
    }

    [Fact]
    public async Task NoSignatureMark_IsFlagged()
    {
        var id = AddRecord(ocr: o => o.SignatureMarkDetected = false);

        await CheckAsync(id);

        var flag = Assert.Single(OpenFlags(id));
        Assert.Equal(FlagType.MissingSignature, flag.Type);
        Assert.Equal("No signature mark was found in the Received by box.", flag.Message);
    }

    [Fact]
    public async Task EmployeeConfirmsSignature_OverridesTheScan()
    {
        var id = AddRecord(r => r.SignaturePresent = true, o => o.SignatureMarkDetected = false);

        await CheckAsync(id);

        Assert.Empty(OpenFlags(id));
    }

    [Fact]
    public async Task EmployeeSaysNoSignature_IsFlagged()
    {
        var id = AddRecord(r => r.SignaturePresent = false);

        await CheckAsync(id);

        Assert.Equal(FlagType.MissingSignature, Assert.Single(OpenFlags(id)).Type);
    }

    [Fact]
    public async Task HardToReadField_AsksForADoubleCheck()
    {
        var id = AddRecord(ocr: o => o.ZoneConfidence = 0.55f);

        await CheckAsync(id);

        var flag = Assert.Single(OpenFlags(id));
        Assert.Equal(FlagType.LowConfidence, flag.Type);
        Assert.Equal("Zone", flag.Field);
        Assert.Equal("Please double-check the zone. That part of the scan was hard to read.", flag.Message);
    }

    [Fact]
    public async Task HardToReadField_CorrectedByEmployee_IsNoLongerFlagged()
    {
        var id = AddRecord(r => r.Zone = "East", o => { o.Zone = "Eazt"; o.ZoneConfidence = 0.55f; });

        await CheckAsync(id);

        Assert.Empty(OpenFlags(id));
    }

    [Fact]
    public async Task UnreadableSheet_GetsOneClearFlag_NotOnePerField()
    {
        var id = AddRecord(
            r => { r.Zone = r.OrderNumber = r.DeliverTo = r.DeliveryInstructions = null; r.TotalProducts = null; },
            o => { o.OcrFailed = true; o.Zone = o.OrderNumber = o.DeliverTo = o.TotalProducts = o.DeliveryInstructions = null; o.SignatureMarkDetected = null; });

        await CheckAsync(id);

        var flag = Assert.Single(OpenFlags(id));
        Assert.Equal(FlagType.OcrFailed, flag.Type);
        Assert.Equal("This sheet couldn't be read automatically. Please type in the details.", flag.Message);
    }

    [Fact]
    public async Task CheckingAgain_DoesNotDuplicateFlags_AndFixedProblemsAreMarkedResolved()
    {
        var id = AddRecord(r => r.Zone = null);
        await CheckAsync(id);
        await CheckAsync(id);
        Assert.Single(OpenFlags(id));

        using (var db = _testDb.NewContext())
        {
            db.DeliveryConfirmations.Single(c => c.Id == id).Zone = "East";
            db.SaveChanges();
        }
        await CheckAsync(id);

        Assert.Empty(OpenFlags(id));
        using var check = _testDb.NewContext();
        var history = check.ValidationFlags.Single(f => f.DeliveryConfirmationId == id);
        Assert.NotNull(history.ResolvedAt); // kept, not deleted
        Assert.Null(history.ResolvedByUserId);
        Assert.Equal("Fixed. Checked again automatically.", history.ResolutionNote);
    }

    [Fact]
    public async Task DuplicateFileFlagFromUpload_IsKept()
    {
        var id = AddRecord();
        using (var db = _testDb.NewContext())
        {
            db.ValidationFlags.Add(new ValidationFlag
            {
                DeliveryConfirmationId = id, Type = FlagType.DuplicateFile,
                Message = "This exact file was already uploaded on Nov 9, 2026.", CreatedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        await CheckAsync(id);

        Assert.Equal(FlagType.DuplicateFile, Assert.Single(OpenFlags(id)).Type);
    }

    [Fact]
    public async Task ReviewedRecords_AreNotChecked()
    {
        var id = AddRecord(r => r.Zone = null, status: ConfirmationStatus.Rejected);

        await CheckAsync(id);

        Assert.Empty(OpenFlags(id));
    }

    [Fact]
    public async Task CheckAllWaiting_ChecksEveryPendingRecord()
    {
        var a = AddRecord(r => r.Zone = null);
        var b = AddRecord(r => r.OrderNumber = "MF-77777", o => o.OrderNumber = "MF-77777");

        using (var db = _testDb.NewContext())
            await new ConfirmationValidator(db, TimeProvider.System).CheckAllWaitingAsync(CancellationToken.None);

        Assert.Equal(FlagType.MissingField, Assert.Single(OpenFlags(a)).Type);
        Assert.Equal(FlagType.OrderNotFound, Assert.Single(OpenFlags(b)).Type);
    }
}
