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
/// in-memory database and uses a known test-only demo password.
/// </summary>
public class TestWebAppFactory : WebApplicationFactory<Program>
{
    public const string DemoPassword = "Test-Only-Passw0rd!";

    private readonly string _databaseName = $"tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Confirmations", "Server=not-used-in-tests");
        builder.UseSetting("DemoUsers:Password", DemoPassword);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
