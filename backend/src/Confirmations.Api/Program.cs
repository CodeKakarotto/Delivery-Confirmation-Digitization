using Confirmations.Api.Auth;
using Confirmations.Api.Data;
using Confirmations.Api.Scans;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Database: SQL Server Express with Windows Authentication.
// The connection string lives in User Secrets (see SETUP.md), never in the repo.
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Confirmations")
        ?? throw new InvalidOperationException(
            "Connection string 'Confirmations' is not set. See SETUP.md, section 'Database and demo password'.");
    options.UseSqlServer(connectionString);
});

// Login: ASP.NET Core Identity (hashed passwords stored in our database).
builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "mf_confirmations_auth";
    options.Cookie.HttpOnly = true; // JavaScript can't read the login cookie
    // Local development runs over plain http through the Vite proxy.
    // Step 10 (Cloudflare, HTTPS) switches this to Always + SameSite=None.
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    // This is an API: answer 401/403 instead of redirecting to a login page.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddConfirmationPolicies();

// Scan uploads: private folder on the host computer (see SETUP.md).
builder.Services.Configure<ScanStorageOptions>(builder.Configuration.GetSection("ScanStorage"));
builder.Services.AddSingleton<IScanStorage, FileSystemScanStorage>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ScanUploadService>();

var app = builder.Build();

await DemoDataSeeder.InitializeAsync(app.Services);

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
