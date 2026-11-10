using System.Globalization;
using System.Security.Cryptography;
using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Scans;

public sealed record ScanUploadResult(string FileName, bool Saved, Guid? ScanId, bool Duplicate, string Message);

/// <summary>
/// Checks one uploaded file, removes hidden metadata, stores it privately,
/// and creates a Pending record for an employee to review.
/// </summary>
public class ScanUploadService(AppDbContext db, IScanStorage storage, TimeProvider clock)
{
    public const string Saved = "Saved.";
    public const string Empty = "This file is empty.";
    public const string TooLarge = "This file is larger than 10 MB.";
    public const string NotAnImage = "This file isn't a JPEG or PNG picture.";
    public const string Damaged = "This picture looks damaged and can't be used.";

    public async Task<ScanUploadResult> UploadAsync(IFormFile file, string userId, CancellationToken cancellationToken)
    {
        var fileName = CleanFileName(file.FileName);

        if (file.Length == 0)
            return Rejected(fileName, Empty);
        if (file.Length > ScanStorageOptions.MaxFileBytes)
            return Rejected(fileName, TooLarge);

        byte[] original;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, cancellationToken);
            original = buffer.ToArray();
        }
        if (original.Length > ScanStorageOptions.MaxFileBytes)
            return Rejected(fileName, TooLarge);

        var contentType = ScanFileInspector.DetectImageType(original);
        if (contentType is null)
            return Rejected(fileName, NotAnImage);

        var cleaned = contentType == ScanFileInspector.Jpeg
            ? ImageMetadataStripper.StripJpeg(original)
            : ImageMetadataStripper.StripPng(original);
        if (cleaned is null)
            return Rejected(fileName, Damaged);

        // Fingerprint of the file exactly as uploaded, to recognise the same file uploaded again.
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(original));
        var earlierUpload = await db.ScanDocuments.IgnoreQueryFilters()
            .Where(s => s.Sha256 == sha256)
            .OrderBy(s => s.UploadedAt)
            .Select(s => (DateTime?)s.UploadedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var extension = contentType == ScanFileInspector.Jpeg ? ".jpg" : ".png";
        var storedFileName = await storage.SaveAsync(cleaned, extension, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        var scan = new ScanDocument
        {
            OriginalFileName = fileName,
            StoredFileName = storedFileName,
            ContentType = contentType,
            SizeBytes = cleaned.Length,
            PageCount = 1,
            Sha256 = sha256,
            UploadedByUserId = userId,
            UploadedAt = now,
        };
        var confirmation = new DeliveryConfirmation
        {
            ScanDocument = scan,
            Status = ConfirmationStatus.Pending,
            CreatedAt = now,
        };

        var duplicate = earlierUpload is not null;
        if (duplicate)
        {
            var when = earlierUpload!.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
            confirmation.Flags.Add(new ValidationFlag
            {
                Type = FlagType.DuplicateFile,
                Message = $"This exact file was already uploaded on {when}.",
                CreatedAt = now,
            });
        }

        db.ScanDocuments.Add(scan);
        db.DeliveryConfirmations.Add(confirmation);
        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = userId,
            Action = AuditAction.Uploaded,
            EntityType = nameof(ScanDocument),
            EntityId = scan.Id.ToString(),
            Timestamp = now,
        });
        await db.SaveChangesAsync(cancellationToken);

        return new ScanUploadResult(fileName, true, scan.Id, duplicate,
            duplicate ? "Saved. This exact file was uploaded before, so it's marked for a second look." : Saved);
    }

    private static ScanUploadResult Rejected(string fileName, string message) =>
        new(fileName, false, null, false, message);

    /// <summary>
    /// Keeps only the last part of the name the browser sent (no folders), without
    /// control characters. Used for display only – never as a path on disk.
    /// </summary>
    internal static string CleanFileName(string? raw)
    {
        var name = (raw ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0 || name is "." or "..")
            name = "unnamed file";
        return name.Length > 255 ? name[..255] : name;
    }
}
