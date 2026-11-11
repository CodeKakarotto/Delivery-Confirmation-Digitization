using System.Net.Http.Headers;
using System.Net.Http.Json;
using Confirmations.Api.Data;
using Confirmations.Api.Ocr;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Confirmations.Api.Tests.Ocr;

/// <summary>The app with background OCR switched on, using simulated Tesseract.</summary>
public sealed class BackgroundOcrFactory : TestWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Ocr:RunInBackground", "true");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITesseractRunner>();
            services.AddSingleton<ITesseractRunner>(FakeTesseractRunner.Returning(TsvBuilder.StandardTicket()));
            services.RemoveAll<ISignatureMarkDetector>();
            services.AddSingleton<ISignatureMarkDetector>(new FakeSignatureDetector(new SignatureCheck(true, 0.9f)));
        });
    }
}

/// <summary>Step 5: uploads are read automatically, without slowing down the upload.</summary>
public class OcrBackgroundTests(BackgroundOcrFactory factory) : IClassFixture<BackgroundOcrFactory>
{
    private sealed record UploadResult(Guid? ScanId, bool Saved);
    private sealed record UploadResponse(List<UploadResult> Results);

    [Fact]
    public async Task UploadedSheet_IsReadInTheBackground()
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = "employee@demo.local", password = TestWebAppFactory.DemoPassword })).EnsureSuccessStatusCode();

        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(TestImages.Jpeg());
        part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(part, "files", "sheet.jpg");
        var upload = await (await client.PostAsync("/api/scans", form)).Content.ReadFromJsonAsync<UploadResponse>();
        var scanId = upload!.Results.Single().ScanId!.Value;

        // Wait (up to 10 s) for the background reader to finish.
        string? zone = null;
        for (var i = 0; i < 100 && zone is null; i++)
        {
            await Task.Delay(100);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            zone = await db.DeliveryConfirmations.Where(c => c.ScanDocumentId == scanId).Select(c => c.Zone).SingleAsync();
        }

        Assert.Equal("North", zone);
    }
}
