using System.Net;
using System.Net.Http.Json;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Tests.Infrastructure;

/// <summary>
/// An active engagement with a consultant, applicable controls that have active criteria, and one ready
/// document. Each control has its own framework, so tests never share criteria.
/// </summary>
public sealed class RunScenario : IAsyncDisposable
{
    public const string PolicyText = "The information security policy is reviewed every year by the board.";

    private RunScenario(PostgresFixture postgres)
    {
        Db = postgres.CreateDbContext();
        Seed = new Seed(Db);
        Factory = new ApiFactory(postgres);
    }

    public AppDbContext Db { get; }
    public Seed Seed { get; }
    public ApiFactory Factory { get; }
    public User Consultant { get; private set; } = null!;
    public Client Client { get; private set; } = null!;
    public Engagement Engagement { get; private set; } = null!;
    public List<Control> Controls { get; } = [];
    public Document Document { get; private set; } = null!;
    public DocumentChunk Chunk { get; private set; } = null!;
    public HttpClient Api { get; private set; } = null!;

    public string RunsUrl => $"/api/engagements/{Engagement.Id}/runs";

    public static async Task<RunScenario> CreateAsync(
        PostgresFixture postgres, int controls = 2, EngagementStatus status = EngagementStatus.Active, bool criteria = true)
    {
        var scenario = new RunScenario(postgres);
        var seed = scenario.Seed;
        scenario.Consultant = await seed.ConsultantAsync();
        scenario.Client = await seed.ClientAsync();
        scenario.Engagement = await seed.EngagementAsync(scenario.Consultant, scenario.Client, status);
        for (var i = 0; i < controls; i++)
        {
            var control = await seed.ControlAsync();
            await seed.EngagementControlAsync(scenario.Engagement, control);
            if (criteria)
            {
                await seed.CriteriaAsync(control, scenario.Consultant);
            }

            scenario.Controls.Add(control);
        }

        scenario.Document = await seed.DocumentAsync(scenario.Engagement, scenario.Consultant, DocumentStatus.Ready);
        scenario.Chunk = await seed.ChunkAsync(scenario.Document, 0, PolicyText);
        scenario.Api = scenario.Factory.CreateClientFor(scenario.Consultant);
        return scenario;
    }

    /// <summary>Starts a run and returns its ID, after checking the answer is 202.</summary>
    public async Task<Guid> StartRunAsync(HttpClient? api = null)
    {
        var response = await (api ?? Api).PostAsync(RunsUrl, null);
        await response.AssertStatusAsync(HttpStatusCode.Accepted);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    /// <summary>Starts a run and handles every message, as a worker would.</summary>
    public async Task<Guid> StartAndAssessAsync()
    {
        var runId = await StartRunAsync();
        await Factory.HandleQueuedAssessmentsAsync();
        return runId;
    }

    public async Task<AssessmentRun> RunAsync(Guid runId)
    {
        Db.ChangeTracker.Clear();
        return await Db.AssessmentRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
    }

    public async Task<List<Assessment>> AssessmentsAsync(Guid runId)
    {
        Db.ChangeTracker.Clear();
        return await Db.Assessments.AsNoTracking().Where(a => a.RunId == runId).ToListAsync();
    }

    public async Task<List<EvidenceCitation>> CitationsAsync(Guid assessmentId)
    {
        Db.ChangeTracker.Clear();
        return await Db.EvidenceCitations.AsNoTracking().Where(c => c.AssessmentId == assessmentId).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Db.DisposeAsync();
    }
}
