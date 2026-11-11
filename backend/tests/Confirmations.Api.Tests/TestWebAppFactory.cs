using Confirmations.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Confirmations.Api.Tests;

/// <summary>
/// Runs the real API in memory, but swaps SQL Server for a throwaway
/// in-memory database, uses a known test-only demo password, and stores
/// uploaded scans in a temporary folder that is removed afterwards.
/// </summary>
public class TestWebAppFactory : WebApplicationFactory<Program>
{
    public const string DemoPassword = "Test-Only-Passw0rd!";

    private readonly string _databaseName = $"tests-{Guid.NewGuid()}";

    public string ScanFolder { get; } =
        Path.Combine(Path.GetTempPath(), "mf-confirmation-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Confirmations", "Server=not-used-in-tests");
        builder.UseSetting("DemoUsers:Password", DemoPassword);
        builder.UseSetting("ScanStorage:Folder", ScanFolder);
        // Tests run OCR explicitly; the background reader is off unless a test turns it on.
        builder.UseSetting("Ocr:RunInBackground", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(ScanFolder))
            Directory.Delete(ScanFolder, recursive: true);
    }
}
