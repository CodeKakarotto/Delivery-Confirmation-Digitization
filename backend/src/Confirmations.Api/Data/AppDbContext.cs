using Confirmations.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Data;

/// <summary>
/// The app's SQL Server database: login tables (Step 2) plus the
/// delivery confirmation tables (Step 3).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<ScanDocument> ScanDocuments => Set<ScanDocument>();
    public DbSet<OcrResult> OcrResults => Set<OcrResult>();
    public DbSet<DeliveryConfirmation> DeliveryConfirmations => Set<DeliveryConfirmation>();
    public DbSet<ValidationFlag> ValidationFlags => Set<ValidationFlag>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Order>(order =>
        {
            order.HasIndex(o => o.OrderNumber).IsUnique();
            order.Property(o => o.OrderNumber).HasMaxLength(50);
            order.Property(o => o.Zone).HasMaxLength(50);
            order.Property(o => o.DeliverTo).HasMaxLength(300);
        });

        builder.Entity<ScanDocument>(scan =>
        {
            scan.HasIndex(s => s.StoredFileName).IsUnique();
            scan.HasIndex(s => s.Sha256);
            scan.HasIndex(s => s.UploadedAt);
            scan.Property(s => s.OriginalFileName).HasMaxLength(255);
            scan.Property(s => s.StoredFileName).HasMaxLength(100);
            scan.Property(s => s.ContentType).HasMaxLength(100);
            scan.Property(s => s.Sha256).HasMaxLength(64);

            // Hidden scans are left out of every normal query.
            scan.HasQueryFilter(s => !s.IsDeleted);

            scan.HasOne<IdentityUser>().WithMany().HasForeignKey(s => s.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
            scan.HasOne<IdentityUser>().WithMany().HasForeignKey(s => s.DeletedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OcrResult>(ocr =>
        {
            ocr.HasOne(o => o.ScanDocument).WithOne(s => s.OcrResult)
                .HasForeignKey<OcrResult>(o => o.ScanDocumentId).OnDelete(DeleteBehavior.Restrict);
            ocr.Property(o => o.Zone).HasMaxLength(100);
            ocr.Property(o => o.OrderNumber).HasMaxLength(100);
            ocr.Property(o => o.DeliverTo).HasMaxLength(500);
            ocr.Property(o => o.TotalProducts).HasMaxLength(50);
            ocr.Property(o => o.DeliveryInstructions).HasMaxLength(2000);
        });

        builder.Entity<DeliveryConfirmation>(confirmation =>
        {
            // One confirmation record per scan.
            confirmation.HasOne(c => c.ScanDocument).WithOne(s => s.Confirmation)
                .HasForeignKey<DeliveryConfirmation>(c => c.ScanDocumentId).OnDelete(DeleteBehavior.Restrict);

            confirmation.HasOne(c => c.Order).WithMany()
                .HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.Restrict);

            // One *approved* confirmation per order (rejected/voided ones may sit beside it).
            confirmation.HasIndex(c => c.OrderId).IsUnique().HasFilter("[Status] = 'Approved'");
            confirmation.HasIndex(c => c.Status);
            confirmation.HasIndex(c => c.OrderNumber);

            confirmation.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            confirmation.Property(c => c.Zone).HasMaxLength(50);
            confirmation.Property(c => c.OrderNumber).HasMaxLength(50);
            confirmation.Property(c => c.DeliverTo).HasMaxLength(300);
            confirmation.Property(c => c.DeliveryInstructions).HasMaxLength(1000);
            confirmation.Property(c => c.ManagerNote).HasMaxLength(1000);
            confirmation.Property(c => c.VoidReason).HasMaxLength(1000);
            confirmation.Property(c => c.Version).IsConcurrencyToken();

            confirmation.HasOne<IdentityUser>().WithMany().HasForeignKey(c => c.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
            confirmation.HasOne<IdentityUser>().WithMany().HasForeignKey(c => c.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
            confirmation.HasOne<IdentityUser>().WithMany().HasForeignKey(c => c.VoidedByUserId).OnDelete(DeleteBehavior.Restrict);

            // Same visibility as its scan.
            confirmation.HasQueryFilter(c => !c.ScanDocument.IsDeleted);
        });

        builder.Entity<ValidationFlag>(flag =>
        {
            flag.HasOne(f => f.DeliveryConfirmation).WithMany(c => c.Flags)
                .HasForeignKey(f => f.DeliveryConfirmationId).OnDelete(DeleteBehavior.Restrict);
            flag.Property(f => f.Type).HasConversion<string>().HasMaxLength(30);
            flag.Property(f => f.Field).HasMaxLength(50);
            flag.Property(f => f.Message).HasMaxLength(500);
            flag.Property(f => f.ResolutionNote).HasMaxLength(1000);
            flag.HasIndex(f => f.Type);
            flag.HasOne<IdentityUser>().WithMany().HasForeignKey(f => f.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
            flag.HasQueryFilter(f => !f.DeliveryConfirmation.ScanDocument.IsDeleted);
        });

        builder.Entity<OcrResult>().HasQueryFilter(o => !o.ScanDocument.IsDeleted);

        builder.Entity<AuditLogEntry>(audit =>
        {
            audit.ToTable("AuditLog");
            audit.Property(a => a.Action).HasConversion<string>().HasMaxLength(30);
            audit.Property(a => a.EntityType).HasMaxLength(50);
            audit.Property(a => a.EntityId).HasMaxLength(50);
            audit.Property(a => a.ChangedFields).HasMaxLength(500);
            audit.HasIndex(a => new { a.EntityType, a.EntityId });
            audit.HasOne<IdentityUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampNewVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampNewVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Give every edited confirmation a new Version. The save only succeeds if
    /// the old Version is still in the database, i.e. nobody else saved first.
    /// </summary>
    private void StampNewVersions()
    {
        foreach (var entry in ChangeTracker.Entries<DeliveryConfirmation>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.Version = Guid.NewGuid();
        }
    }
}
