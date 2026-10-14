using System.Net;
using System.Net.Http.Json;
using App.Tests.Infrastructure;

namespace App.Tests;

[Collection(PostgresCollection.Name)]
public class ClientTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OnlyAdmins_CreateClients()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var admin = await seed.AdminAsync();
        var consultant = await seed.ConsultantAsync();
        await using var factory = new ApiFactory(postgres);

        var created = await factory.CreateClientFor(admin).PostAsJsonAsync("/api/clients", new { name = "Acme" });
        var byConsultant = await factory.CreateClientFor(consultant).PostAsJsonAsync("/api/clients", new { name = "Acme" });

        await created.AssertStatusAsync(HttpStatusCode.Created);
        await byConsultant.AssertStatusAsync(HttpStatusCode.Forbidden);
        var id = (await created.JsonAsync()).GetProperty("id").GetGuid();
        await (await factory.CreateClientFor(consultant).GetAsync($"/api/clients/{id}")).AssertStatusAsync(HttpStatusCode.OK);
        await (await factory.CreateClientFor(consultant).GetAsync($"/api/clients/{Guid.NewGuid()}"))
            .AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task Lists_PageWithACursor()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        for (var i = 0; i < 3; i++)
        {
            await seed.ClientAsync();
        }

        await using var factory = new ApiFactory(postgres);
        var api = factory.CreateClientFor(consultant);

        var first = await (await api.GetAsync("/api/clients?limit=2")).JsonAsync();
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await (await api.GetAsync($"/api/clients?limit=2&cursor={cursor}")).JsonAsync();

        var firstIds = first.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        var secondIds = second.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(2, firstIds.Count);
        Assert.NotNull(cursor);
        Assert.NotEmpty(secondIds);
        Assert.Empty(firstIds.Intersect(secondIds));

        await (await api.GetAsync("/api/clients?cursor=not-a-cursor")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        await (await api.GetAsync("/api/clients?limit=0")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        await (await api.GetAsync("/api/clients?limit=101")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
    }
}
