using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Confirmations.Api.Tests;

/// <summary>
/// Step 1: the API exposes a simple health check so the frontend
/// (and the host teammate) can confirm the backend is running.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_ReturnsStatusOkInJson()
    {
        var body = await _client.GetFromJsonAsync<HealthResponse>("/api/health");

        Assert.NotNull(body);
        Assert.Equal("OK", body!.Status);
    }

    private sealed record HealthResponse(string Status);
}
