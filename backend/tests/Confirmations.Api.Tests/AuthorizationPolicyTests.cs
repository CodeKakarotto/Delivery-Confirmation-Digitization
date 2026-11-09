using System.Security.Claims;
using Confirmations.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Confirmations.Api.Tests;

/// <summary>
/// Step 2: the role rules every future endpoint will use.
/// </summary>
public class AuthorizationPolicyTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AuthorizationPolicyTests(TestWebAppFactory factory) => _factory = factory;

    private static ClaimsPrincipal UserWithRole(string? role)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "someone@demo.local") };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    [Theory]
    [InlineData(Roles.Employee, Policies.EmployeeOrManager, true)]
    [InlineData(Roles.Manager, Policies.EmployeeOrManager, true)]
    [InlineData(Roles.Driver, Policies.EmployeeOrManager, false)]
    [InlineData(null, Policies.EmployeeOrManager, false)]
    [InlineData(Roles.Employee, Policies.ManagerOnly, false)]
    [InlineData(Roles.Manager, Policies.ManagerOnly, true)]
    [InlineData(Roles.Driver, Policies.ManagerOnly, false)]
    public async Task Policy_AllowsOnlyTheRightRoles(string? role, string policy, bool expectedAllowed)
    {
        var auth = _factory.Services.GetRequiredService<IAuthorizationService>();

        var result = await auth.AuthorizeAsync(UserWithRole(role), policy);

        Assert.Equal(expectedAllowed, result.Succeeded);
    }

    [Fact]
    public async Task EndpointsWithoutAnExplicitRule_BlockDrivers()
    {
        // Any future endpoint that forgets to say who may use it
        // still blocks drivers and logged-out users.
        var options = _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value;
        var auth = _factory.Services.GetRequiredService<IAuthorizationService>();

        Assert.NotNull(options.FallbackPolicy);
        Assert.False((await auth.AuthorizeAsync(UserWithRole(Roles.Driver), options.FallbackPolicy!)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(UserWithRole(Roles.Driver), options.DefaultPolicy)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(UserWithRole(Roles.Employee), options.DefaultPolicy)).Succeeded);
    }

    [Fact]
    public async Task Startup_CreatesTheThreeRoles()
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        Assert.True(await roles.RoleExistsAsync(Roles.Employee));
        Assert.True(await roles.RoleExistsAsync(Roles.Manager));
        Assert.True(await roles.RoleExistsAsync(Roles.Driver));
    }
}
