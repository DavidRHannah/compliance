using System.Net;
using App.Data.Entities;
using App.Integrations.Abstractions;
using App.Pipeline;
using App.Pipeline.Assessments;
using App.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Tests;

/// <summary>Run endpoints and the worker around them: progress, completion, flags, the sweeper and the consumer.</summary>
[Collection(PostgresCollection.Name)]
public class RunTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Runs_AreListedWithProgress()
    {
        await using var s = await RunScenario.CreateAsync(postgres);
        var runId = await s.StartRunAsync();

        var started = await (await s.Api.GetAsync($"{s.RunsUrl}/{runId}")).JsonAsync();
        Assert.Equal("queued", started.GetProperty("status").GetString());
        Assert.Equal(2, started.GetProperty("progress").GetProperty("total").GetInt32());
        Assert.Equal(0, started.GetProperty("progress").GetProperty("assessed").GetInt32());

        await s.Factory.HandleQueuedAssessmentsAsync();

        var done = await (await s.Api.GetAsync($"{s.RunsUrl}/{runId}")).JsonAsync();
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.Equal(2, done.GetProperty("progress").GetProperty("assessed").GetInt32());
        Assert.Equal("fake-model", done.GetProperty("modelId").GetString());
        var listResponse = await s.Api.GetAsync(s.RunsUrl);
        await listResponse.AssertStatusAsync(HttpStatusCode.OK);
        var list = await listResponse.JsonAsync();
        Assert.Equal(runId, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Run_OfAnotherEngagement_IsNotFound()
    {
        await using var s = await RunScenario.CreateAsync(postgres);
        var other = await s.Seed.EngagementAsync(s.Consultant, s.Client, EngagementStatus.Active);
        var foreign = await s.Seed.RunAsync(other, s.Consultant);

        await (await s.Api.GetAsync($"{s.RunsUrl}/{foreign.Id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        await (await s.Api.PostAsync($"{s.RunsUrl}/{foreign.Id}/resume", null)).AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task Runs_FollowTheAccessCheckOrder()
    {
        await using var s = await RunScenario.CreateAsync(postgres);
        var otherClientUser = await s.Seed.ClientUserAsync(await s.Seed.ClientAsync());
        var ownClientUser = await s.Seed.ClientUserAsync(s.Client);

        await (await s.Factory.CreateClientFor(otherClientUser).PostAsync(s.RunsUrl, null))
            .AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        await (await s.Factory.CreateClientFor(ownClientUser).PostAsync(s.RunsUrl, null))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        await (await s.Factory.CreateClientFor(ownClientUser).GetAsync(s.RunsUrl))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Run_WithNoApplicableControls_IsCompletedAtOnce()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 0);

        var runId = await s.StartRunAsync();

        Assert.Equal(RunStatus.Completed, (await s.RunAsync(runId)).Status);
        Assert.Empty(s.Factory.Queue.Assessments());
    }

    [Fact]
    public async Task Run_FailsOnlyWhenEveryControlHasFinished()
    {
        await using var s = await RunScenario.CreateAsync(postgres);
        var runId = await s.StartRunAsync();
        var messages = s.Factory.Queue.Assessments();
        await using var scope = s.Factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<AssessmentWriter>();

        await writer.RecordFailureAsync(runId, s.Engagement.Id, messages[0].ControlId, "The model returned no valid result.", CancellationToken.None);
        Assert.Equal(RunStatus.Queued, (await s.RunAsync(runId)).Status);

        await s.Factory.HandleAsync(messages[1]);
        var run = await s.RunAsync(runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.CompletedAt);
        var failed = await s.Db.AssessmentRunControls.AsNoTracking().SingleAsync(rc => rc.RunId == runId && rc.ControlId == messages[0].ControlId);
        Assert.Equal("The model returned no valid result.", failed.Error);

        // A control that already has an assessment is never marked failed.
        await writer.RecordFailureAsync(runId, s.Engagement.Id, messages[1].ControlId, "Late.", CancellationToken.None);
        Assert.Null((await s.Db.AssessmentRunControls.AsNoTracking().SingleAsync(rc => rc.RunId == runId && rc.ControlId == messages[1].ControlId)).FailedAt);
    }

    [Fact]
    public async Task SupersedingADecidedAssessment_AddsAFlagThatNamesIt()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var first = (await s.AssessmentsAsync(await s.StartAndAssessAsync())).Single();
        Assert.False(await s.Db.ReviewFlags.AnyAsync(f => f.AssessmentId == first.Id));

        s.Db.ConsultantDecisions.Add(new ConsultantDecision
        {
            AssessmentId = first.Id, DecidedById = s.Consultant.Id, FinalVerdict = FinalVerdict.Pass, DecidedAt = DateTimeOffset.UtcNow,
        });
        await s.Db.SaveChangesAsync();

        var second = (await s.AssessmentsAsync(await s.StartAndAssessAsync())).Single();
        var flag = await s.Db.ReviewFlags.AsNoTracking().SingleAsync(f => f.AssessmentId == second.Id);
        Assert.Equal(ReviewFlagReason.DecisionSuperseded, flag.Reason);
        Assert.Equal(first.Id, flag.SupersededAssessmentId);
        Assert.Null(flag.ClearedAt);

        // The third assessment replaces one with no decision, so it is not flagged.
        var third = (await s.AssessmentsAsync(await s.StartAndAssessAsync())).Single();
        Assert.False(await s.Db.ReviewFlags.AnyAsync(f => f.AssessmentId == third.Id));
    }

    [Fact]
    public async Task Sweeper_FailsStalledRuns_AndTheyCanBeResumed()
    {
        await using var s = await RunScenario.CreateAsync(postgres);
        var stalled = await s.StartRunAsync();
        var other = await s.Seed.EngagementAsync(s.Consultant, s.Client, EngagementStatus.Active);
        var recent = await s.Seed.RunAsync(other, s.Consultant);
        await s.Db.AssessmentRuns.Where(r => r.Id == stalled)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.CreatedAt, DateTimeOffset.UtcNow.AddHours(-1)));

        var failed = await Sweeper.FailStalledRunsAsync(s.Db, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30), CancellationToken.None);

        Assert.True(failed >= 1);
        Assert.Equal(RunStatus.Failed, (await s.RunAsync(stalled)).Status);
        Assert.Equal(RunStatus.Queued, (await s.RunAsync(recent.Id)).Status);

        await (await s.Api.PostAsync($"{s.RunsUrl}/{stalled}/resume", null)).AssertStatusAsync(HttpStatusCode.Accepted);
        Assert.Equal(4, s.Factory.Queue.Assessments().Count);
        await s.Factory.HandleQueuedAssessmentsAsync();
        Assert.Equal(RunStatus.Completed, (await s.RunAsync(stalled)).Status);
    }

    [Fact]
    public async Task Sweeper_LeavesARunThatIsMakingProgress()
    {
        await using var s = await RunScenario.CreateAsync(postgres);
        var runId = await s.StartRunAsync();
        await s.Factory.HandleAsync(s.Factory.Queue.Assessments()[0]);
        await s.Db.AssessmentRuns.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.CreatedAt, DateTimeOffset.UtcNow.AddHours(-1)).SetProperty(r => r.StartedAt, DateTimeOffset.UtcNow.AddHours(-1)));

        await Sweeper.FailStalledRunsAsync(s.Db, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30), CancellationToken.None);

        // One assessment was saved a moment ago, so the run is still alive.
        Assert.Equal(RunStatus.Running, (await s.RunAsync(runId)).Status);
    }

    [Fact]
    public async Task Consumer_DeletesDoneMessages_AndRecordsFailureOnTheThirdReceive()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        s.Factory.Assessor.Respond = _ => throw new AssessmentFailedException("The model returned no valid result.");
        var runId = await s.StartRunAsync();
        var message = Assert.Single(s.Factory.Queue.Assessments());
        var receiver = new FakeQueueReceiver();
        var consumer = new AssessmentConsumer(
            s.Factory.Services.GetRequiredService<IServiceScopeFactory>(), receiver,
            Options.Create(new WorkerSettings { Concurrency = 4 }), NullLogger<AssessmentConsumer>.Instance);

        // First and second receive: the message is left on the queue and nothing is recorded.
        foreach (var receiveCount in new[] { 1, 2 })
        {
            receiver.Next = [new ReceivedMessage<AssessControlMessage>(message, $"r{receiveCount}", receiveCount)];
            await consumer.ProcessBatchAsync(CancellationToken.None);
        }

        Assert.Empty(receiver.Deleted);
        Assert.Null((await s.Db.AssessmentRunControls.AsNoTracking().SingleAsync(rc => rc.RunId == runId)).FailedAt);
        Assert.Equal(RunStatus.Running, (await s.RunAsync(runId)).Status);

        // Third receive: the failure is recorded, the run fails, and SQS moves the message to the dead-letter queue.
        receiver.Next = [new ReceivedMessage<AssessControlMessage>(message, "r3", 3)];
        await consumer.ProcessBatchAsync(CancellationToken.None);

        Assert.Empty(receiver.Deleted);
        var control = await s.Db.AssessmentRunControls.AsNoTracking().SingleAsync(rc => rc.RunId == runId);
        Assert.NotNull(control.FailedAt);
        Assert.Equal("The model returned no valid result.", control.Error);
        Assert.Equal(RunStatus.Failed, (await s.RunAsync(runId)).Status);

        // A message that works is deleted.
        s.Factory.Assessor.Respond = _ => new AssessorResult(PreliminaryVerdict.InterpretationRequired, "Unclear.", []);
        await (await s.Api.PostAsync($"{s.RunsUrl}/{runId}/resume", null)).AssertStatusAsync(HttpStatusCode.Accepted);
        receiver.Next = [new ReceivedMessage<AssessControlMessage>(message, "ok", 1)];
        await consumer.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(["ok"], receiver.Deleted);
        Assert.Equal(RunStatus.Completed, (await s.RunAsync(runId)).Status);
    }

    [Fact]
    public async Task Consumer_StoresOnlyTheTypeOfAnUnexpectedError()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        s.Factory.Assessor.Respond = _ => throw new InvalidOperationException("secret document text and a quote");
        var runId = await s.StartRunAsync();
        var receiver = new FakeQueueReceiver { Next = [new ReceivedMessage<AssessControlMessage>(s.Factory.Queue.Assessments()[0], "r3", 3)] };
        var consumer = new AssessmentConsumer(
            s.Factory.Services.GetRequiredService<IServiceScopeFactory>(), receiver,
            Options.Create(new WorkerSettings()), NullLogger<AssessmentConsumer>.Instance);

        await consumer.ProcessBatchAsync(CancellationToken.None);

        var control = await s.Db.AssessmentRunControls.AsNoTracking().SingleAsync(rc => rc.RunId == runId);
        Assert.Equal(nameof(InvalidOperationException), control.Error);
    }

    private sealed class FakeQueueReceiver : IQueueReceiver
    {
        public List<ReceivedMessage<AssessControlMessage>> Next { get; set; } = [];
        public List<string> Deleted { get; } = [];

        public Task<IReadOnlyList<ReceivedMessage<TMessage>>> ReceiveAsync<TMessage>(string queue, int max, CancellationToken ct)
        {
            var batch = Next.Cast<ReceivedMessage<TMessage>>().ToList();
            Next = [];
            return Task.FromResult<IReadOnlyList<ReceivedMessage<TMessage>>>(batch);
        }

        public Task DeleteAsync(string queue, string receipt, CancellationToken ct)
        {
            Deleted.Add(receipt);
            return Task.CompletedTask;
        }
    }
}
