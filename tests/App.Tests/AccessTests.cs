using System.Net;
using System.Security.Claims;
using System.Text.Json;
using App.Common.Access;
using App.Tests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests;

[Collection(PostgresCollection.Name)]
public class AccessTests(PostgresFixture postgres)
{
    [Fact]
    [Requirement("AC-1", "Every request, except the health check, must carry a valid token from the identity provider.")]
    public async Task RequestWithoutToken_IsRejected()
    {
        await using var db = postgres.CreateDbContext();
        var user = await new Seed(db).ConsultantAsync();
        await using var factory = new ApiFactory(postgres);

        var noToken = await factory.CreateClient().GetAsync("/api/me");
        var expired = await factory.CreateClientWithToken(
            TestTokens.Create(user.ExternalId, expired: true)).GetAsync("/api/me");
        var wrongKey = await factory.CreateClientWithToken(
            TestTokens.Create(user.ExternalId, key: TestTokens.NewKey())).GetAsync("/api/me");
        var wrongIssuer = await factory.CreateClientWithToken(
            TestTokens.Create(user.ExternalId, issuer: "https://cognito-idp.test.amazonaws.com/other-pool")).GetAsync("/api/me");
        var otherClient = await factory.CreateClientWithToken(
            TestTokens.Create(user.ExternalId, clientId: "someone-else")).GetAsync("/api/me");
        var idToken = await factory.CreateClientWithToken(
            TestTokens.Create(user.ExternalId, tokenUse: "id")).GetAsync("/api/me");
        var valid = await factory.CreateClientFor(user).GetAsync("/api/me");
        var health = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongIssuer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherClient.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, idToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-2", "Every user must sign in with multi-factor authentication.")]
    public void SignIn_RequiresMfa()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-3", "Users are created by invitation. There is no self-registration.")]
    public void Users_AreCreatedOnlyByInvitation()
    {
    }

    [Fact]
    [Requirement("AC-4", "A user's role and client are held by the backend, not by the identity provider.")]
    public async Task RoleAndClient_ComeFromBackend()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var client = await seed.ClientAsync();
        var clientUser = await seed.ClientUserAsync(client);
        await using var factory = new ApiFactory(postgres);
        // The token claims admin; the backend must ignore it.
        var token = TestTokens.Create(clientUser.ExternalId, extraClaims: new Claim("cognito:groups", "admin"));

        var response = await factory.CreateClientWithToken(token).GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("client", body.RootElement.GetProperty("role").GetString());
        Assert.Equal(client.Id, body.RootElement.GetProperty("clientId").GetGuid());
    }

    // Covers the engagement routes. The list is not built yet; see EngagementList_ShowsClientUserOnlyOwnClient.
    [Fact]
    [Requirement("AC-5", "A client user can access only the engagements of their own client.")]
    public async Task ClientUser_SeesOnlyOwnClientsEngagements()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var myClient = await seed.ClientAsync();
        var otherClient = await seed.ClientAsync();
        var clientUser = await seed.ClientUserAsync(myClient);
        var mine = await seed.EngagementAsync(consultant, myClient);
        var theirs = await seed.EngagementAsync(consultant, otherClient);
        await using var factory = new ApiFactory(postgres);

        var asClient = factory.CreateClientFor(clientUser);
        var asConsultant = factory.CreateClientFor(consultant);

        Assert.Equal(HttpStatusCode.OK, (await asClient.GetAsync($"/api/engagements/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await asClient.GetAsync($"/api/engagements/{theirs.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await asClient.GetAsync($"/api/engagements/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await asConsultant.GetAsync($"/api/engagements/{theirs.Id}")).StatusCode);
    }

    // The second half of AC-5. Fill in with GET /api/engagements, then fold it into the AC-5 test.
    [Fact(Skip = "Pending: engagement list")]
    public void EngagementList_ShowsClientUserOnlyOwnClient()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-6", "No operation may read or use data from an engagement other than the one requested.")]
    public void Operations_NeverUseAnotherEngagementsData()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-7", "An admin can disable a user. A disabled user cannot sign in, and the backend refuses their requests. An admin cannot disable themselves or the last admin. A disabled user can be enabled again.")]
    public void Admin_CanDisableAndEnableUsers()
    {
    }

    [Fact]
    public async Task DisabledUser_WithValidToken_Gets403()
    {
        await using var db = postgres.CreateDbContext();
        var user = await new Seed(db).DisabledConsultantAsync();
        await using var factory = new ApiFactory(postgres);

        var response = await factory.CreateClientFor(user).GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ValidToken_ForUnknownUser_Gets401()
    {
        await using var factory = new ApiFactory(postgres);

        var response = await factory.CreateClientWithToken(TestTokens.Create(Guid.NewGuid().ToString()))
            .GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EveryApiRoute_RunsTheAccessCheckAndDeclaresItsRoles()
    {
        await using var factory = new ApiFactory(postgres);

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api") == true)
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, e =>
        {
            Assert.NotNull(e.Metadata.GetMetadata<AccessChecked>());
            Assert.NotNull(e.Metadata.GetMetadata<AllowedRoles>());
        });
    }
}
