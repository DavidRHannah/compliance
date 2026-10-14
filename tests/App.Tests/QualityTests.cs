using App.Data.Entities;
using App.Pipeline.Assessments;
using App.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests;

[Collection(PostgresCollection.Name)]
public class QualityTests(PostgresFixture postgres)
{
    [Fact]
    [Requirement("QR-1", "Stored files, search and assessments are separated by engagement. A test must show that a search in one engagement cannot return another engagement's content.")]
    public async Task Search_NeverReturnsAnotherEngagementsContent()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var client = await seed.ClientAsync();
        var engagementA = await seed.EngagementAsync(consultant, client);
        var engagementB = await seed.EngagementAsync(consultant, client);

        // The same text in both engagements, so only the engagement filter tells them apart.
        var chunkA = await seed.ChunkAsync(await seed.DocumentAsync(engagementA, consultant, DocumentStatus.Ready), 0, RunScenario.PolicyText);
        var chunkB = await seed.ChunkAsync(await seed.DocumentAsync(engagementB, consultant, DocumentStatus.Ready), 0, RunScenario.PolicyText);

        var queries = new[] { "information security policy" };
        var inA = await EvidenceSearch.FindAsync(db, new FakeEmbedder(), engagementA.Id, queries, CancellationToken.None);
        var inB = await EvidenceSearch.FindAsync(db, new FakeEmbedder(), engagementB.Id, queries, CancellationToken.None);

        Assert.Equal([chunkA.Id], inA);
        Assert.Equal([chunkB.Id], inB);
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-2", "All data is encrypted in transit and at rest.")]
    public void Data_IsEncryptedInTransitAndAtRest()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-3", "The backend holds no long-lived credentials. It reaches AWS services through its assigned role.")]
    public void Backend_HoldsNoLongLivedCredentials()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-4", "Every model call goes through Amazon Bedrock in the firm's own AWS account.")]
    public void ModelCalls_GoThroughBedrock()
    {
    }

    [Fact]
    [Requirement("QR-5", "A message may be delivered more than once without causing duplicate or incorrect data.")]
    public async Task RepeatedMessage_CausesNoDuplicates()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var runId = await s.StartRunAsync();
        var message = Assert.Single(s.Factory.Queue.Assessments());

        await s.Factory.HandleAsync(message);
        await s.Factory.HandleAsync(message);

        var assessment = Assert.Single(await s.AssessmentsAsync(runId));
        Assert.Single(await s.CitationsAsync(assessment.Id));
        Assert.Single(s.Factory.Assessor.Contexts);
        Assert.Equal(RunStatus.Completed, (await s.RunAsync(runId)).Status);

        // Two deliveries that run at the same time: the second save is refused, not duplicated.
        await using var scope = s.Factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<AssessmentWriter>();
        Assert.False(await writer.SaveAsync(
            new AssessmentToSave(runId, s.Engagement.Id, message.ControlId, PreliminaryVerdict.InterpretationRequired, "Again.", []),
            CancellationToken.None));
        Assert.Single(await s.AssessmentsAsync(runId));
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-7", "Every verdict can be traced to its run, model, prompt version, assessment criteria version, and cited passages.")]
    public void Verdict_IsTraceable()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-8", "Each request and background job writes logs that share one correlation ID.")]
    public void Logs_ShareCorrelationId()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-9", "Targets will be set after the first working version is measured.")]
    public void Performance_TargetsPending()
    {
    }
}
