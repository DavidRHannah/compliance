using System.Net;
using System.Net.Http.Json;
using App.Data.Entities;
using App.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace App.Tests;

/// <summary>Engagement setup rules that back requirements without being one.</summary>
[Collection(PostgresCollection.Name)]
public class ScopeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ScopeAndApplicabilityChanges_AreAudited()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        await seed.Iso27001Async();
        var consultant = await seed.ConsultantAsync();
        var client = await seed.ClientAsync();
        await using var factory = new ApiFactory(postgres);
        var api = factory.CreateClientFor(consultant);
        var engagementId = (await (await api.PostAsJsonAsync("/api/engagements", new { clientId = client.Id, name = "E" }))
            .JsonAsync()).GetProperty("id").GetGuid();
        var controlId = await db.EngagementControls
            .Where(ec => ec.EngagementId == engagementId).Select(ec => ec.ControlId).FirstAsync();

        await (await api.PostAsJsonAsync($"/api/engagements/{engagementId}/scope-items", new { type = "asset", name = "Laptops" }))
            .AssertStatusAsync(HttpStatusCode.Created);
        await (await api.PutAsJsonAsync($"/api/engagements/{engagementId}/controls/{controlId}",
            new { isApplicable = false, justification = "Out of scope." })).AssertStatusAsync(HttpStatusCode.OK);

        var actions = await db.AuditEvents.AsNoTracking()
            .Where(a => a.EngagementId == engagementId && a.ActorId == consultant.Id)
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Contains("engagement.created", actions);
        Assert.Contains("scope_item.created", actions);
        Assert.Contains("applicability.set", actions);
    }

    [Fact]
    public async Task ClientUser_OnApplicability_Gets404ForOtherClient_And403ForOwn()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var client = await seed.ClientAsync();
        var clientUser = await seed.ClientUserAsync(client);
        var own = await seed.EngagementAsync(consultant, client);
        var other = await seed.EngagementAsync(consultant);
        await using var factory = new ApiFactory(postgres);
        var api = factory.CreateClientFor(clientUser);
        var body = new { isApplicable = true };

        await (await api.PutAsJsonAsync($"/api/engagements/{other.Id}/controls/{Guid.NewGuid()}", body))
            .AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        await (await api.PutAsJsonAsync($"/api/engagements/{own.Id}/controls/{Guid.NewGuid()}", body))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Close_IsRefused_WhileARunIsActive()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var engagement = await seed.EngagementAsync(consultant, await seed.ClientAsync(), EngagementStatus.Active);
        await seed.RunAsync(engagement, consultant, RunStatus.Running);
        await using var factory = new ApiFactory(postgres);

        await (await factory.CreateClientFor(consultant).PostAsync($"/api/engagements/{engagement.Id}/close", null))
            .AssertProblemAsync(HttpStatusCode.Conflict, "run-active");
    }
}
