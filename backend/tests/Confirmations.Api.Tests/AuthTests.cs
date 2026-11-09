using System.Net;
using System.Net.Http.Json;

namespace Confirmations.Api.Tests;

/// <summary>
/// Step 2: login, logout, "who am I", and driver blocking.
/// Each test uses its own HttpClient so login cookies don't leak between tests.
/// </summary>
public class AuthTests : IClassFixture<TestWebAppFactory>
{
    private const string Employee = "employee@demo.local";
    private const string Manager = "manager@demo.local";
    private const string Driver = "driver@demo.local";

    private readonly TestWebAppFactory _factory;

    public AuthTests(TestWebAppFactory factory) => _factory = factory;

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    [Theory]
    [InlineData(Employee, "Employee")]
    [InlineData(Manager, "Manager")]
    public async Task Login_WithCorrectPassword_ReturnsEmailAndRole(string email, string expectedRole)
    {
        var client = _factory.CreateClient();

        var response = await LoginAsync(client, email, TestWebAppFactory.DemoPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserInfo>();
        Assert.Equal(email, user!.Email);
        Assert.Equal(expectedRole, user.Role);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await LoginAsync(client, Employee, "Wrong-Passw0rd!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUserAndWrongPassword_GiveTheSameMessage()
    {
        // Same message either way, so nobody can use the login form to
        // discover which email addresses have accounts.
        var wrongPassword = await LoginAsync(_factory.CreateClient(), Employee, "Wrong-Passw0rd!");
        var unknownUser = await LoginAsync(_factory.CreateClient(), "nobody@demo.local", "Wrong-Passw0rd!");

        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        var a = await wrongPassword.Content.ReadFromJsonAsync<ErrorMessage>();
        var b = await unknownUser.Content.ReadFromJsonAsync<ErrorMessage>();
        Assert.Equal("Incorrect email or password.", a!.Message);
        Assert.Equal(a.Message, b!.Message);
    }

    [Fact]
    public async Task Login_AsDriver_IsRefusedAndNotSignedIn()
    {
        var client = _factory.CreateClient();

        var response = await LoginAsync(client, Driver, TestWebAppFactory.DemoPassword);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorMessage>();
        Assert.Equal("Driver accounts can't use the delivery confirmation app.", error!.Message);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutLogin_Returns401NotARedirect()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_AfterLogin_ReturnsTheSignedInUser()
    {
        var client = _factory.CreateClient();
        await LoginAsync(client, Manager, TestWebAppFactory.DemoPassword);

        var user = await client.GetFromJsonAsync<UserInfo>("/api/auth/me");

        Assert.Equal(Manager, user!.Email);
        Assert.Equal("Manager", user.Role);
    }

    [Fact]
    public async Task Me_IgnoresClientSuppliedIdentity()
    {
        var client = _factory.CreateClient();
        await LoginAsync(client, Employee, TestWebAppFactory.DemoPassword);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/me?email={Manager}&role=Manager");
        request.Headers.Add("X-User-Id", Manager);
        request.Headers.Add("X-User-Role", "Manager");
        var response = await client.SendAsync(request);

        var user = await response.Content.ReadFromJsonAsync<UserInfo>();
        Assert.Equal(Employee, user!.Email);
        Assert.Equal("Employee", user.Role);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var client = _factory.CreateClient();
        await LoginAsync(client, Employee, TestWebAppFactory.DemoPassword);

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Health_StillWorksWithoutLogin()
    {
        var response = await _factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record UserInfo(string Email, string Role);
    private sealed record ErrorMessage(string Message);
}
