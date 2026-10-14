using System.Net;
using App.Tests.Infrastructure;

namespace App.Tests;

[Collection(PostgresCollection.Name)]
public class HealthTests(PostgresFixture postgres)
{
    [Fact]
    [Requirement("OP-1", "The API must expose /health, which reports whether the service is running.")]
    public async Task Health_ReturnsOk()
    {
        await using var factory = new ApiFactory(postgres);

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
