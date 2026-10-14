using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using App.Common.Access;
using App.Data.Entities;
using App.Tests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    [Requirement("AC-3", "Users are created by invitation. There is no self-registration.")]
    public async Task Users_AreCreatedOnlyByInvitation()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var admin = await seed.AdminAsync();
        var consultant = await seed.ConsultantAsync();
        var client = await seed.ClientAsync();
        await using var factory = new ApiFactory(postgres);
        var asAdmin = factory.CreateClientFor(admin);
        var email = $"{Guid.NewGuid()}@example.test";
        var invite = new { email, displayName = "New User", role = "client", clientId = client.Id };

        var created = await asAdmin.PostAsJsonAsync("/api/users", invite);
        var byConsultant = await factory.CreateClientFor(consultant).PostAsJsonAsync("/api/users",
            invite with { email = $"{Guid.NewGuid()}@example.test" });
        var sameEmailOtherCase = await asAdmin.PostAsJsonAsync("/api/users", invite with { email = email.ToUpperInvariant() });

        await created.AssertStatusAsync(HttpStatusCode.Created);
        await byConsultant.AssertStatusAsync(HttpStatusCode.Forbidden);
        await sameEmailOtherCase.AssertProblemAsync(HttpStatusCode.Conflict, "email-taken");

        // The Cognito account was made first, and the row holds its subject.
        Assert.Contains(("invite", email), factory.Directory.Calls);
        var userId = (await created.JsonAsync()).GetProperty("id").GetGuid();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal(UserRole.Client, user.Role);
        Assert.Equal(client.Id, user.ClientId);
        Assert.False(string.IsNullOrEmpty(user.ExternalId));

        // The invitation can be sent again until it is accepted.
        (await asAdmin.PostAsync($"/api/users/{userId}/resend-invite", null)).EnsureSuccessStatusCode();
        factory.Directory.Accepted[user.ExternalId] = true;
        await (await asAdmin.PostAsync($"/api/users/{userId}/resend-invite", null))
            .AssertProblemAsync(HttpStatusCode.Conflict, "invite-accepted");
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

        // The list holds only the client's own engagements, across every page.
        var listed = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await (await asClient.GetAsync($"/api/engagements?limit=100{(cursor is null ? "" : $"&cursor={cursor}")}"))
                .JsonAsync();
            listed.AddRange(page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal([mine.Id], listed);
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-6", "No operation may read or use data from an engagement other than the one requested.")]
    public void Operations_NeverUseAnotherEngagementsData()
    {
    }

    // The last-admin rule can only be reached by two admins disabling each other at once, so its
    // decision is unit-tested in UserTests.DisableCheck_RefusesLastAdmin.
    [Fact]
    [Requirement("AC-7", "An admin can disable a user. A disabled user cannot sign in, and the backend refuses their requests. An admin cannot disable themselves or the last admin. A disabled user can be enabled again.")]
    public async Task Admin_CanDisableAndEnableUsers()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var admin = await seed.AdminAsync();
        var consultant = await seed.ConsultantAsync();
        await using var factory = new ApiFactory(postgres);
        var asAdmin = factory.CreateClientFor(admin);
        var asConsultant = factory.CreateClientFor(consultant);

        await (await asAdmin.PostAsync($"/api/users/{admin.Id}/disable", null))
            .AssertProblemAsync(HttpStatusCode.Conflict, "cannot-disable-self");

        await (await asAdmin.PostAsync($"/api/users/{consultant.Id}/disable", null)).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await asConsultant.GetAsync("/api/me")).AssertProblemAsync(HttpStatusCode.Forbidden, "user-disabled");
        Assert.Contains(("disable", consultant.ExternalId), factory.Directory.Calls);

        await (await asAdmin.PostAsync($"/api/users/{consultant.Id}/enable", null)).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await asConsultant.GetAsync("/api/me")).AssertStatusAsync(HttpStatusCode.OK);
        Assert.Contains(("enable", consultant.ExternalId), factory.Directory.Calls);
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
