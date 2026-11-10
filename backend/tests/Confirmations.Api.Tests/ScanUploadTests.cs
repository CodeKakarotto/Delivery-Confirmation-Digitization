using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Confirmations.Api.Data;
using Confirmations.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Confirmations.Api.Tests;

/// <summary>Step 4: uploading scans through the API and viewing them back.</summary>
public class ScanUploadTests : IClassFixture<TestWebAppFactory>
{
    private const string Employee = "employee@demo.local";
    private readonly TestWebAppFactory _factory;

    public ScanUploadTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record UploadResult(string FileName, bool Saved, Guid? ScanId, bool Duplicate, string Message);
    private sealed record UploadResponse(List<UploadResult> Results);
    private sealed record ErrorMessage(string Message);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = Employee, password = TestWebAppFactory.DemoPassword });
        login.EnsureSuccessStatusCode();
        return client;
    }

    private static MultipartFormDataContent Form(params (string FileName, byte[] Bytes, string ClaimedType)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (fileName, bytes, claimedType) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(claimedType);
            form.Add(part, "files", fileName);
        }
        return form;
    }

    private static async Task<UploadResult> SingleResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UploadResponse>();
        return Assert.Single(body!.Results);
    }

    private T WithDb<T>(Func<AppDbContext, T> query)
    {
        using var scope = _factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Upload_WithoutSigningIn_IsRefused()
    {
        var response = await _factory.CreateClient()
            .PostAsync("/api/scans", Form(("sheet.jpg", TestImages.Jpeg(), "image/jpeg")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ValidJpeg_IsSavedPrivately_WithServerTimeAndUser()
    {
        var client = await SignedInClientAsync();
        var before = DateTime.UtcNow;
        var form = Form(("sheet-001.jpg", TestImages.Jpeg(), "image/jpeg"));
        form.Add(new StringContent("someone-else"), "uploadedByUserId"); // must be ignored

        var result = await SingleResultAsync(await client.PostAsync("/api/scans", form));

        Assert.True(result.Saved);
        Assert.False(result.Duplicate);
        Assert.Equal("Saved.", result.Message);
        Assert.Equal("sheet-001.jpg", result.FileName);

        var employeeId = WithDb(db => db.Users.Single(u => u.Email == Employee).Id);
        var scan = WithDb(db => db.ScanDocuments.Single(s => s.Id == result.ScanId));
        Assert.Equal(employeeId, scan.UploadedByUserId);
        Assert.InRange(scan.UploadedAt, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal("image/jpeg", scan.ContentType);
        Assert.Equal(64, scan.Sha256.Length);

        // Stored under a random name, not the original one, and without metadata.
        Assert.NotEqual("sheet-001.jpg", scan.StoredFileName);
        var storedPath = Path.Combine(_factory.ScanFolder, scan.StoredFileName);
        Assert.True(File.Exists(storedPath));
        Assert.False(TestImages.ContainsSecret(File.ReadAllBytes(storedPath)));

        // A Pending record is waiting for review, and the upload is in the audit log.
        var confirmation = WithDb(db => db.DeliveryConfirmations.Single(c => c.ScanDocumentId == scan.Id));
        Assert.Equal(ConfirmationStatus.Pending, confirmation.Status);
        var audit = WithDb(db => db.AuditLog.Single(a => a.EntityId == scan.Id.ToString()));
        Assert.Equal(AuditAction.Uploaded, audit.Action);
        Assert.Equal(employeeId, audit.UserId);
    }

    [Fact]
    public async Task Upload_ValidPng_IsSaved()
    {
        var client = await SignedInClientAsync();

        var result = await SingleResultAsync(
            await client.PostAsync("/api/scans", Form(("sheet.png", TestImages.Png(), "image/png"))));

        Assert.True(result.Saved);
        var scan = WithDb(db => db.ScanDocuments.Single(s => s.Id == result.ScanId));
        Assert.Equal("image/png", scan.ContentType);
        Assert.EndsWith(".png", scan.StoredFileName);
    }

    [Theory]
    [InlineData("invoice.jpg", "%PDF-1.7 not allowed", "This file isn't a JPEG or PNG picture.")]
    [InlineData("photo.jpg", "MZ fake program", "This file isn't a JPEG or PNG picture.")]
    [InlineData("empty.jpg", "", "This file is empty.")]
    public async Task Upload_DisguisedOrEmptyFiles_AreRejected(string fileName, string content, string expected)
    {
        var client = await SignedInClientAsync();
        var bytes = System.Text.Encoding.Latin1.GetBytes(content);

        var result = await SingleResultAsync(
            await client.PostAsync("/api/scans", Form((fileName, bytes, "image/jpeg"))));

        Assert.False(result.Saved);
        Assert.Null(result.ScanId);
        Assert.Equal(expected, result.Message);
    }

    [Fact]
    public async Task Upload_DamagedPicture_IsRejected()
    {
        var client = await SignedInClientAsync();

        var result = await SingleResultAsync(await client.PostAsync("/api/scans",
            Form(("broken.jpg", TestImages.Jpeg()[..20], "image/jpeg"))));

        Assert.False(result.Saved);
        Assert.Equal("This picture looks damaged and can't be used.", result.Message);
    }

    [Fact]
    public async Task Upload_FileOver10MB_IsRejected()
    {
        var client = await SignedInClientAsync();
        var big = new byte[10 * 1024 * 1024 + 1];
        TestImages.Jpeg().CopyTo(big, 0);

        var result = await SingleResultAsync(
            await client.PostAsync("/api/scans", Form(("huge.jpg", big, "image/jpeg"))));

        Assert.False(result.Saved);
        Assert.Equal("This file is larger than 10 MB.", result.Message);
    }

    [Fact]
    public async Task Upload_SameFileTwice_SecondIsSavedButFlaggedAsDuplicate()
    {
        var client = await SignedInClientAsync();
        var sheet = TestImages.Jpeg();

        var first = await SingleResultAsync(await client.PostAsync("/api/scans", Form(("a.jpg", sheet, "image/jpeg"))));
        var second = await SingleResultAsync(await client.PostAsync("/api/scans", Form(("a-again.jpg", sheet, "image/jpeg"))));

        Assert.False(first.Duplicate);
        Assert.True(second.Saved);
        Assert.True(second.Duplicate);
        Assert.StartsWith("Saved. This exact file was uploaded before", second.Message);

        var flags = WithDb(db => db.ValidationFlags
            .Where(f => f.DeliveryConfirmation.ScanDocumentId == second.ScanId).ToList());
        Assert.Equal(FlagType.DuplicateFile, Assert.Single(flags).Type);
    }

    [Fact]
    public async Task Upload_SeveralFiles_EachGetsItsOwnResult()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/api/scans", Form(
            ("good.jpg", TestImages.Jpeg(), "image/jpeg"),
            ("bad.jpg", "not a picture"u8.ToArray(), "image/jpeg"),
            ("good.png", TestImages.Png(), "image/png")));

        var body = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.Equal([true, false, true], body!.Results.Select(r => r.Saved));
    }

    [Fact]
    public async Task Upload_NoFiles_Returns400()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/api/scans", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorMessage>();
        Assert.Equal("Please choose at least one file.", error!.Message);
    }

    [Fact]
    public async Task Upload_MoreThan20Files_Returns400()
    {
        var client = await SignedInClientAsync();
        var files = Enumerable.Range(1, 21).Select(i => ($"s{i}.jpg", TestImages.Jpeg(), "image/jpeg")).ToArray();

        var response = await client.PostAsync("/api/scans", Form(files));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorMessage>();
        Assert.Equal("Please upload 20 files or fewer at a time.", error!.Message);
    }

    [Fact]
    public async Task Upload_RequestOverTheSizeLimit_Returns413WithPlainMessage()
    {
        var client = await SignedInClientAsync();
        var tooBig = new byte[96 * 1000 * 1000]; // over the 95 MB request limit

        var response = await client.PostAsync("/api/scans", Form(("way-too-big.jpg", tooBig, "image/jpeg")));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorMessage>();
        Assert.Equal("This upload is too large. Please upload fewer or smaller files (up to 10 MB each).", error!.Message);
    }

    [Fact]
    public async Task Upload_FileNameWithPathsAndHtml_IsCleanedAndNeverUsedOnDisk()
    {
        var client = await SignedInClientAsync();

        var result = await SingleResultAsync(await client.PostAsync("/api/scans",
            Form((@"..\..\<script>alert(1)</script>.jpg", TestImages.Jpeg(), "image/jpeg"))));

        Assert.True(result.Saved);
        Assert.DoesNotContain("..", result.FileName);
        Assert.DoesNotContain("\\", result.FileName);
        var scan = WithDb(db => db.ScanDocuments.Single(s => s.Id == result.ScanId));
        Assert.Matches("^[0-9a-f]{32}\\.jpg$", scan.StoredFileName);
    }

    [Fact]
    public async Task ScanImage_IsShownToEmployees_WithSafeHeaders()
    {
        var client = await SignedInClientAsync();
        var upload = await SingleResultAsync(
            await client.PostAsync("/api/scans", Form(("view.png", TestImages.Png(), "image/png"))));

        var response = await client.GetAsync($"/api/scans/{upload.ScanId}/image");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.False(TestImages.ContainsSecret(await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task ScanImage_WithoutSigningIn_IsRefused()
    {
        var client = await SignedInClientAsync();
        var upload = await SingleResultAsync(
            await client.PostAsync("/api/scans", Form(("private.jpg", TestImages.Jpeg(), "image/jpeg"))));

        var response = await _factory.CreateClient().GetAsync($"/api/scans/{upload.ScanId}/image");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ScanImage_UnknownOrHiddenScan_Returns404()
    {
        var client = await SignedInClientAsync();
        var upload = await SingleResultAsync(
            await client.PostAsync("/api/scans", Form(("hide-me.jpg", TestImages.Jpeg(), "image/jpeg"))));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ScanDocuments.Single(s => s.Id == upload.ScanId).IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var unknown = await client.GetAsync($"/api/scans/{Guid.NewGuid()}/image");
        var hidden = await client.GetAsync($"/api/scans/{upload.ScanId}/image");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }
}
