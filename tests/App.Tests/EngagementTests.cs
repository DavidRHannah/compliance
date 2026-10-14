using System.Net;
using System.Net.Http.Json;
using App.Data.Entities;
using App.Integrations.Abstractions;
using App.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace App.Tests;

[Collection(PostgresCollection.Name)]
public class EngagementTests(PostgresFixture postgres)
{
    [Fact]
    [Requirement("EN-1", "A consultant can create an engagement for a client.")]
    public async Task Consultant_CanCreateEngagement()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        await seed.Iso27001Async();
        var consultant = await seed.ConsultantAsync();
        var client = await seed.ClientAsync();
        var clientUser = await seed.ClientUserAsync(client);
        await using var factory = new ApiFactory(postgres);
        var request = new { clientId = client.Id, name = "ISO 27001 2026" };

        var created = await factory.CreateClientFor(consultant).PostAsJsonAsync("/api/engagements", request);
        var byClientUser = await factory.CreateClientFor(clientUser).PostAsJsonAsync("/api/engagements", request);
        var unknownClient = await factory.CreateClientFor(consultant)
            .PostAsJsonAsync("/api/engagements", request with { clientId = Guid.NewGuid() });

        await created.AssertStatusAsync(HttpStatusCode.Created);
        var body = await created.JsonAsync();
        Assert.Equal("draft", body.GetProperty("status").GetString());
        Assert.Equal(client.Id, body.GetProperty("clientId").GetGuid());
        Assert.Equal(consultant.Id, body.GetProperty("leadConsultantId").GetGuid());
        await byClientUser.AssertStatusAsync(HttpStatusCode.Forbidden);
        await unknownClient.AssertProblemAsync(HttpStatusCode.BadRequest, "invalid-reference");
    }

    [Fact]
    [Requirement("EN-2", "An engagement is in one of three states: draft, active or closed. A closed engagement is read-only. An engagement moves from draft to active to closed, and never back. A run can start only while it is active.")]
    public async Task Engagement_MovesDraftActiveClosedOnly()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var engagement = await seed.EngagementAsync(consultant);
        var draft = await seed.EngagementAsync(consultant);
        await using var factory = new ApiFactory(postgres);
        var api = factory.CreateClientFor(consultant);
        var path = $"/api/engagements/{engagement.Id}";

        await (await api.PostAsync($"/api/engagements/{draft.Id}/close", null))
            .AssertProblemAsync(HttpStatusCode.Conflict, "invalid-transition");

        await (await api.PostAsync($"{path}/activate", null)).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await api.PostAsync($"{path}/activate", null)).AssertProblemAsync(HttpStatusCode.Conflict, "invalid-transition");
        await (await api.PostAsync($"{path}/close", null)).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await api.PostAsync($"{path}/activate", null)).AssertProblemAsync(HttpStatusCode.Conflict, "invalid-transition");
        await (await api.PostAsync($"{path}/close", null)).AssertProblemAsync(HttpStatusCode.Conflict, "invalid-transition");

        // Closed is read-only.
        await (await api.PostAsJsonAsync($"{path}/scope-items", new { type = "system", name = "CRM" }))
            .AssertProblemAsync(HttpStatusCode.Conflict, "engagement-closed");
        await (await api.PatchAsJsonAsync(path, new { name = "Renamed" }))
            .AssertProblemAsync(HttpStatusCode.Conflict, "engagement-closed");
    }

    // The rest of EN-2. Fill in with POST …/runs, then fold it into the EN-2 test.
    [Fact(Skip = "Pending: runs")]
    public void RunStart_RequiresActiveEngagement()
    {
    }

    [Fact]
    [Requirement("EN-3", "Every ISO 27001 control is applicable to a new engagement by default.")]
    public async Task NewEngagement_HasEveryControlApplicable()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var framework = await seed.Iso27001Async();
        var consultant = await seed.ConsultantAsync();
        var client = await seed.ClientAsync();
        await using var factory = new ApiFactory(postgres);
        var api = factory.CreateClientFor(consultant);

        var created = await (await api.PostAsJsonAsync("/api/engagements", new { clientId = client.Id, name = "E" })).JsonAsync();
        var controls = await (await api.GetAsync($"/api/engagements/{created.GetProperty("id").GetGuid()}/controls?limit=100"))
            .JsonAsync();

        var items = controls.GetProperty("items").EnumerateArray().ToList();
        var expected = await db.Controls.CountAsync(c => c.FrameworkId == framework.Id);
        Assert.Equal(expected, items.Count);
        Assert.All(items, c => Assert.True(c.GetProperty("isApplicable").GetBoolean()));
    }

    [Fact]
    [Requirement("EN-4", "A consultant can mark a control as not applicable, and must give a justification.")]
    public async Task NotApplicable_RequiresJustification()
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
        var path = $"/api/engagements/{engagementId}/controls/{controlId}";

        await (await api.PutAsJsonAsync(path, new { isApplicable = false }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "justification-required");
        await (await api.PutAsJsonAsync(path, new { isApplicable = false, justification = "  " }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "justification-required");
        await (await api.PutAsJsonAsync(path, new { isApplicable = false, justification = "No development in scope." }))
            .AssertStatusAsync(HttpStatusCode.OK);

        var row = await db.EngagementControls.AsNoTracking()
            .SingleAsync(ec => ec.EngagementId == engagementId && ec.ControlId == controlId);
        Assert.False(row.IsApplicable);
        Assert.Equal("No development in scope.", row.Justification);
        Assert.Equal(consultant.Id, row.UpdatedById);
    }

    [Fact]
    [Requirement("EN-5", "Consultants and client users can add and edit scope items of type system, asset or vendor.")]
    public async Task ScopeItems_CanBeAddedAndEdited()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var client = await seed.ClientAsync();
        var clientUser = await seed.ClientUserAsync(client);
        var engagement = await seed.EngagementAsync(consultant, client, EngagementStatus.Active);
        await using var factory = new ApiFactory(postgres);
        var path = $"/api/engagements/{engagement.Id}/scope-items";

        var byConsultant = await factory.CreateClientFor(consultant)
            .PostAsJsonAsync(path, new { type = "system", name = "CRM", aliases = new[] { "Salesforce", " ", "salesforce" } });
        await byConsultant.AssertStatusAsync(HttpStatusCode.Created);
        var itemId = (await byConsultant.JsonAsync()).GetProperty("id").GetGuid();

        var asClient = factory.CreateClientFor(clientUser);
        await (await asClient.PostAsJsonAsync(path, new { type = "vendor", name = "AWS" })).AssertStatusAsync(HttpStatusCode.Created);
        var edited = await asClient.PatchAsJsonAsync($"{path}/{itemId}", new { name = "Customer CRM", description = "Sales data" });
        await (await asClient.PostAsJsonAsync(path, new { type = "vendor", name = "AWS" }))
            .AssertProblemAsync(HttpStatusCode.Conflict, "duplicate-scope-item");

        await edited.AssertStatusAsync(HttpStatusCode.OK);
        var item = await db.ScopeItems.AsNoTracking().SingleAsync(s => s.Id == itemId);
        Assert.Equal("Customer CRM", item.Name);
        Assert.Equal(["Salesforce"], item.Aliases);
        Assert.Equal("Sales data", item.Description);

        // Each successful change queues re-linking of the engagement's documents.
        var relinks = factory.Queue.Sent.Count(m => m.Queue == Queues.Documents
            && m.Message is RelinkScopeMessage r && r.EngagementId == engagement.Id);
        Assert.Equal(3, relinks);
    }

    [Fact]
    [Requirement("EN-6", "A scope item that an assessment refers to cannot be deleted.")]
    public async Task ScopeItemInUse_CannotBeDeleted()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var engagement = await seed.EngagementAsync(consultant);
        var used = await seed.ScopeItemAsync(engagement, consultant);
        var unused = await seed.ScopeItemAsync(engagement, consultant);
        await seed.AssessmentAsync(await seed.RunAsync(engagement, consultant), await seed.ControlAsync(), consultant, used);
        await using var factory = new ApiFactory(postgres);
        var api = factory.CreateClientFor(consultant);
        var path = $"/api/engagements/{engagement.Id}/scope-items";

        await (await api.DeleteAsync($"{path}/{used.Id}")).AssertProblemAsync(HttpStatusCode.Conflict, "scope-item-in-use");
        await (await api.DeleteAsync($"{path}/{unused.Id}")).AssertStatusAsync(HttpStatusCode.NoContent);

        Assert.True(await db.ScopeItems.AnyAsync(s => s.Id == used.Id));
        Assert.False(await db.ScopeItems.AnyAsync(s => s.Id == unused.Id));
    }

    [Fact(Skip = "Withdrawn")]
    [Requirement("EN-7", "Withdrawn. The organization profile is removed.")]
    public void OrganizationProfile_Withdrawn()
    {
    }

    [Fact(Skip = "Withdrawn")]
    [Requirement("EN-8", "Withdrawn. Implementation statements are removed.")]
    public void ImplementationStatements_Withdrawn()
    {
    }

    // "No extra access" is covered by AccessCheckTests: the check never reads the lead.
    [Fact]
    [Requirement("EN-9", "The lead consultant must be an active consultant or admin. Leading an engagement gives no extra access.")]
    public async Task LeadConsultant_IsActiveConsultantWithNoExtraAccess()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        await seed.Iso27001Async();
        var consultant = await seed.ConsultantAsync();
        var admin = await seed.AdminAsync();
        var disabled = await seed.DisabledConsultantAsync();
        var client = await seed.ClientAsync();
        var clientUser = await seed.ClientUserAsync(client);
        await using var factory = new ApiFactory(postgres);
        var api = factory.CreateClientFor(consultant);

        Task<HttpResponseMessage> CreateWithLead(Guid lead) =>
            api.PostAsJsonAsync("/api/engagements", new { clientId = client.Id, name = "E", leadConsultantId = lead });

        await (await CreateWithLead(clientUser.Id)).AssertProblemAsync(HttpStatusCode.BadRequest, "invalid-lead");
        await (await CreateWithLead(disabled.Id)).AssertProblemAsync(HttpStatusCode.BadRequest, "invalid-lead");
        await (await CreateWithLead(Guid.NewGuid())).AssertProblemAsync(HttpStatusCode.BadRequest, "invalid-lead");
        var created = await CreateWithLead(admin.Id);
        await created.AssertStatusAsync(HttpStatusCode.Created);

        // Changing the lead follows the same rule.
        var path = $"/api/engagements/{(await created.JsonAsync()).GetProperty("id").GetGuid()}";
        await (await api.PatchAsJsonAsync(path, new { leadConsultantId = clientUser.Id }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "invalid-lead");
        await (await api.PatchAsJsonAsync(path, new { leadConsultantId = consultant.Id })).AssertStatusAsync(HttpStatusCode.OK);
    }
}
