using Confirmations.Api.Auth;
using Confirmations.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Confirmations.Api.Data;

/// <summary>
/// Runs at startup: applies database migrations, creates the three roles,
/// and creates the fake demo accounts using the password from
/// configuration ("DemoUsers:Password", kept in User Secrets).
/// </summary>
public static class DemoDataSeeder
{
    private static readonly (string Email, string Role)[] DemoAccounts =
    [
        ("employee@demo.local", Roles.Employee),
        ("manager@demo.local", Roles.Manager),
        ("driver@demo.local", Roles.Driver),
    ];

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DemoDataSeeder));

        var db = provider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        await SeedFakeOrdersAsync(db);

        var password = provider.GetRequiredService<IConfiguration>()["DemoUsers:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("DemoUsers:Password is not set, so demo accounts were not created. See SETUP.md.");
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var (email, role) in DemoAccounts)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                EnsureSucceeded(await userManager.CreateAsync(user, password), email);
            }
            else if (!await userManager.CheckPasswordAsync(user, password))
            {
                // The host changed the demo password in User Secrets: apply it.
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                EnsureSucceeded(await userManager.ResetPasswordAsync(user, token, password), email);
            }

            if (!await userManager.IsInRoleAsync(user, role))
                EnsureSucceeded(await userManager.AddToRoleAsync(user, role), email);
        }
    }

    /// <summary>
    /// 20 made-up orders (MF-10001 to MF-10020) so order-ID checks can be
    /// tested. Only added when there are no orders at all, so real orders
    /// are never mixed with or overwritten by fake ones.
    /// </summary>
    private static async Task SeedFakeOrdersAsync(AppDbContext db)
    {
        if (await db.Orders.AnyAsync())
            return;

        string[] zones = ["North", "South", "East", "West", "Downtown"];
        var firstDay = new DateOnly(2026, 11, 2);

        for (var i = 1; i <= 20; i++)
        {
            db.Orders.Add(new Order
            {
                OrderNumber = $"MF-{10000 + i}",
                Zone = zones[(i - 1) % zones.Length],
                DeliverTo = $"Test Recipient {i}, {100 + i} Example St",
                TotalProducts = 1 + (i % 4),
                DeliveryDate = firstDay.AddDays((i - 1) / 4),
            });
        }

        await db.SaveChangesAsync();
    }

    private static void EnsureSucceeded(IdentityResult result, string email)
    {
        if (!result.Succeeded)
        {
            // Error descriptions explain password rules; they never contain the password.
            var reasons = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Could not set up demo account {email}: {reasons}");
        }
    }
}
