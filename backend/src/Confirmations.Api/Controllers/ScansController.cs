using Confirmations.Api.Auth;
using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Confirmations.Api.Scans;
using Confirmations.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Controllers;

/// <summary>Upload scanned delivery sheets and view them. Employees and Managers only.</summary>
/// <remarks>
/// No [ApiController] here on purpose: its automatic form validation answers a
/// malformed upload with a technical error before this code runs. This controller
/// reads and checks the upload itself and always replies in plain English.
/// </remarks>
[Route("api/scans")]
[Authorize(Policy = Policies.EmployeeOrManager)]
public class ScansController(
    ScanUploadService uploads,
    AppDbContext db,
    IScanStorage storage,
    UserManager<IdentityUser> userManager,
    TimeProvider clock) : ControllerBase
{
    public sealed record UploadResponse(IReadOnlyList<ScanUploadResult> Results);
    public sealed record ErrorMessage(string Message);

    /// <summary>Most recently saved scans, newest first (the "Orders" box on the upload page).</summary>
    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        var query = db.ScanDocuments.OrderByDescending(s => s.UploadedAt).Take(Math.Clamp(take, 1, 50));
        return Ok(await ScanSummaries.BuildAsync(query, cancellationToken));
    }

    /// <summary>Every scan saved in the last <paramref name="days"/> days (the "All scans" page).</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int days = 84, CancellationToken cancellationToken = default)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-Math.Clamp(days, 1, 366));
        var query = db.ScanDocuments.Where(s => s.UploadedAt >= since).OrderByDescending(s => s.UploadedAt).Take(5000);
        return Ok(await ScanSummaries.BuildAsync(query, cancellationToken));
    }

    [HttpPost]
    [RequestSizeLimit(ScanStorageOptions.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScanStorageOptions.MaxRequestBytes)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        // Too big as a whole: say so plainly (checked before reading anything).
        if (Request.ContentLength > ScanStorageOptions.MaxRequestBytes)
            return TooLarge();

        // Read the form ourselves so an empty or malformed upload gets a plain
        // message instead of ASP.NET's technical validation error.
        IReadOnlyList<IFormFile> files;
        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            files = form.Files.GetFiles("files");
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(); // size unknown up front, but the server's limit was hit while reading
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return TooLarge(); // multipart body length limit
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            files = [];
        }

        if (files.Count == 0)
            return BadRequest(new ErrorMessage("Please choose at least one file."));
        if (files.Count > ScanStorageOptions.MaxFilesPerUpload)
            return BadRequest(new ErrorMessage("Please upload 20 files or fewer at a time."));

        // The uploader always comes from the server-side login, never from the form.
        var userId = userManager.GetUserId(User)!;

        var results = new List<ScanUploadResult>(files.Count);
        foreach (var file in files)
            results.Add(await uploads.UploadAsync(file, userId, cancellationToken));

        return Ok(new UploadResponse(results));
    }

    private ObjectResult TooLarge() =>
        StatusCode(StatusCodes.Status413PayloadTooLarge, new ErrorMessage(
            "This upload is too large. Please upload fewer or smaller files (up to 10 MB each)."));

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> Image(Guid id, CancellationToken cancellationToken)
    {
        // Hidden (soft-deleted) scans are excluded automatically.
        var scan = await db.ScanDocuments.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new { s.StoredFileName, s.ContentType })
            .FirstOrDefaultAsync(cancellationToken);

        var stream = scan is null ? null : storage.OpenRead(scan.StoredFileName);
        if (stream is null)
            return NotFound();

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(stream, scan!.ContentType);
    }
}
