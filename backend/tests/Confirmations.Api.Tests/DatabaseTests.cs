using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Confirmations.Api.Tests;

/// <summary>
/// Step 3: the confirmation database stores everything correctly and
/// enforces its rules (no duplicates, nothing silently overwritten).
/// </summary>
public class DatabaseTests : IDisposable
{
    private readonly SqliteTestDatabase _testDb = new();

    public void Dispose() => _testDb.Dispose();

    private static readonly DateTime UploadTime = new(2026, 11, 9, 15, 30, 0, DateTimeKind.Utc);

    private Order AddOrder(AppDbContext db, string orderNumber = "MF-10001")
    {
        var order = new Order
        {
            OrderNumber = orderNumber,
            Zone = "North",
            DeliverTo = "Test Recipient 1, 100 Example St",
            TotalProducts = 2,
            DeliveryDate = new DateOnly(2026, 11, 9),
        };
        db.Orders.Add(order);
        return order;
    }

    private ScanDocument AddScan(AppDbContext db, string sha256 = "aa11")
    {
        var scan = new ScanDocument
        {
            OriginalFileName = "sheet-001.jpg",
            StoredFileName = $"{Guid.NewGuid():N}.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 123_456,
            PageCount = 1,
            Sha256 = sha256,
            UploadedByUserId = _testDb.UserId,
            UploadedAt = UploadTime,
        };
        db.ScanDocuments.Add(scan);
        return scan;
    }

    private static DeliveryConfirmation AddConfirmation(
        AppDbContext db, ScanDocument scan, Order? order, ConfirmationStatus status)
    {
        var confirmation = new DeliveryConfirmation
        {
            ScanDocument = scan,
            Order = order,
            OrderNumber = order?.OrderNumber,
            Status = status,
            CreatedAt = UploadTime,
        };
        db.DeliveryConfirmations.Add(confirmation);
        return confirmation;
    }

    [Fact]
    public void Confirmation_IsSavedAndReloadedWithEveryField()
    {
        Guid confirmationId;
        using (var db = _testDb.NewContext())
        {
            var order = AddOrder(db);
            var scan = AddScan(db);
            db.OcrResults.Add(new OcrResult
            {
                ScanDocument = scan,
                Zone = "Norht",
                ZoneConfidence = 0.61f,
                OrderNumber = "MF-10001",
                OrderNumberConfidence = 0.97f,
                DeliverTo = "Test Recipient 1, 100 Example St",
                DeliverToConfidence = 0.88f,
                TotalProducts = "2",
                TotalProductsConfidence = 0.93f,
                DeliveryInstructions = "Leave at side door",
                DeliveryInstructionsConfidence = 0.72f,
                SignatureMarkDetected = true,
                SignatureConfidence = 0.80f,
                RawText = "North MF-10001 Test Recipient 1 ...",
                OcrFailed = false,
                ProcessedAt = UploadTime,
            });
            var confirmation = new DeliveryConfirmation
            {
                ScanDocument = scan,
                Order = order,
                Zone = "North",
                OrderNumber = "MF-10001",
                DeliverTo = "Test Recipient 1, 100 Example St",
                TotalProducts = 2,
                DeliveryInstructions = "Leave at side door",
                SignaturePresent = true,
                Status = ConfirmationStatus.Approved,
                ReviewedByUserId = _testDb.UserId,
                ReviewedAt = UploadTime.AddMinutes(5),
                CreatedAt = UploadTime,
            };
            db.DeliveryConfirmations.Add(confirmation);
            db.ValidationFlags.Add(new ValidationFlag
            {
                DeliveryConfirmation = confirmation,
                Type = FlagType.LowConfidence,
                Field = "Zone",
                Message = "Please double-check the zone.",
                CreatedAt = UploadTime,
            });
            db.AuditLog.Add(new AuditLogEntry
            {
                UserId = _testDb.UserId,
                Action = AuditAction.Approved,
                EntityType = nameof(DeliveryConfirmation),
                EntityId = confirmation.Id.ToString(),
                ChangedFields = "Zone",
                Timestamp = UploadTime.AddMinutes(5),
            });
            db.SaveChanges();
            confirmationId = confirmation.Id;
        }

        using (var db = _testDb.NewContext())
        {
            var saved = db.DeliveryConfirmations
                .Include(c => c.Order)
                .Include(c => c.ScanDocument).ThenInclude(s => s.OcrResult)
                .Include(c => c.Flags)
                .Single(c => c.Id == confirmationId);

            Assert.Equal("North", saved.Zone);
            Assert.Equal("MF-10001", saved.OrderNumber);
            Assert.Equal("Test Recipient 1, 100 Example St", saved.DeliverTo);
            Assert.Equal(2, saved.TotalProducts);
            Assert.Equal("Leave at side door", saved.DeliveryInstructions);
            Assert.True(saved.SignaturePresent);
            Assert.Equal(ConfirmationStatus.Approved, saved.Status);
            Assert.Equal(_testDb.UserId, saved.ReviewedByUserId);
            Assert.Equal("MF-10001", saved.Order!.OrderNumber);

            // The original OCR reading is kept alongside the correction.
            var ocr = saved.ScanDocument.OcrResult!;
            Assert.Equal("Norht", ocr.Zone);
            Assert.Equal(0.61f, ocr.ZoneConfidence);
            Assert.Equal("2", ocr.TotalProducts);
            Assert.True(ocr.SignatureMarkDetected);
            Assert.Equal("North MF-10001 Test Recipient 1 ...", ocr.RawText);

            var flag = Assert.Single(saved.Flags);
            Assert.Equal(FlagType.LowConfidence, flag.Type);
            Assert.Equal("Please double-check the zone.", flag.Message);

            var audit = Assert.Single(db.AuditLog);
            Assert.Equal(AuditAction.Approved, audit.Action);
            Assert.Equal(confirmationId.ToString(), audit.EntityId);
        }
    }

    [Fact]
    public void OrderNumbers_MustBeUnique()
    {
        using var db = _testDb.NewContext();
        AddOrder(db, "MF-10001");
        AddOrder(db, "MF-10001");

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void AnOrder_CanHaveOnlyOneApprovedConfirmation()
    {
        using var db = _testDb.NewContext();
        var order = AddOrder(db);
        AddConfirmation(db, AddScan(db, "aa11"), order, ConfirmationStatus.Approved);
        db.SaveChanges();

        AddConfirmation(db, AddScan(db, "bb22"), order, ConfirmationStatus.Approved);

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void AnOrder_CanHaveRejectedOrVoidedRecordsBesideTheApprovedOne()
    {
        using var db = _testDb.NewContext();
        var order = AddOrder(db);
        AddConfirmation(db, AddScan(db, "aa11"), order, ConfirmationStatus.Approved);
        AddConfirmation(db, AddScan(db, "bb22"), order, ConfirmationStatus.Rejected);
        AddConfirmation(db, AddScan(db, "cc33"), order, ConfirmationStatus.Voided);

        db.SaveChanges();

        Assert.Equal(3, db.DeliveryConfirmations.Count());
    }

    [Fact]
    public void AScan_HasAtMostOneConfirmationRecord()
    {
        Guid scanId;
        using (var db = _testDb.NewContext())
        {
            var scan = AddScan(db);
            AddConfirmation(db, scan, null, ConfirmationStatus.Pending);
            db.SaveChanges();
            scanId = scan.Id;
        }

        // A second request tries to create another record for the same scan.
        using var other = _testDb.NewContext();
        other.DeliveryConfirmations.Add(new DeliveryConfirmation
        {
            ScanDocumentId = scanId,
            Status = ConfirmationStatus.Pending,
            CreatedAt = UploadTime,
        });

        Assert.Throws<DbUpdateException>(() => other.SaveChanges());
    }

    [Fact]
    public void DeletedScans_AreHiddenButNotErased()
    {
        using (var db = _testDb.NewContext())
        {
            var scan = AddScan(db);
            scan.IsDeleted = true;
            db.SaveChanges();
        }

        using (var db = _testDb.NewContext())
        {
            Assert.Empty(db.ScanDocuments);
            Assert.Single(db.ScanDocuments.IgnoreQueryFilters());
        }
    }

    [Fact]
    public void TwoPeopleEditingTheSameRecord_SecondSaveIsRejected()
    {
        Guid id;
        using (var db = _testDb.NewContext())
        {
            id = AddConfirmation(db, AddScan(db), null, ConfirmationStatus.Pending).Id;
            db.SaveChanges();
        }

        using var first = _testDb.NewContext();
        using var second = _testDb.NewContext();
        var copyA = first.DeliveryConfirmations.Single(c => c.Id == id);
        var copyB = second.DeliveryConfirmations.Single(c => c.Id == id);

        copyA.Zone = "North";
        first.SaveChanges();

        copyB.Zone = "South";
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    [Fact]
    public void StatusesAndFlagTypes_AreStoredAsReadableText()
    {
        using (var db = _testDb.NewContext())
        {
            var confirmation = AddConfirmation(db, AddScan(db), null, ConfirmationStatus.Flagged);
            db.ValidationFlags.Add(new ValidationFlag
            {
                DeliveryConfirmation = confirmation,
                Type = FlagType.MissingSignature,
                Message = "No signature found.",
                CreatedAt = UploadTime,
            });
            db.SaveChanges();
        }

        Assert.Equal("Flagged", _testDb.ScalarSql("SELECT Status FROM DeliveryConfirmations"));
        Assert.Equal("MissingSignature", _testDb.ScalarSql("SELECT Type FROM ValidationFlags"));
    }
}

/// <summary>Step 3: fake test orders are created at startup (never real customers).</summary>
public class FakeOrderSeedingTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public FakeOrderSeedingTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Startup_CreatesTwentyFakeOrders_OnlyOnce()
    {
        // Startup already ran once; running the seeder again must not add more.
        await DemoDataSeeder.InitializeAsync(_factory.Services);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = db.Orders.OrderBy(o => o.OrderNumber).ToList();

        Assert.Equal(20, orders.Count);
        Assert.Equal("MF-10001", orders.First().OrderNumber);
        Assert.Equal("MF-10020", orders.Last().OrderNumber);
        Assert.All(orders, o => Assert.StartsWith("Test Recipient", o.DeliverTo));
        Assert.All(orders, o => Assert.True(o.TotalProducts > 0));
    }
}
