using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Tests;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Requirement("OP-1", "The API must expose /health, which reports whether the service is running.")]
    public async Task Health_ReturnsOk()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
