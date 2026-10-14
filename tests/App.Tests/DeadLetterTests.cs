using System.Text.Json;
using Amazon.SQS.Model;
using App.Data.Entities;
using App.Integrations.Abstractions;
using App.Integrations.Aws;
using App.Pipeline;
using App.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Tests;

/// <summary>The assessment queue on SQS (LocalStack), with a dead-letter queue and a limit of three receives.</summary>
[Collection(PostgresCollection.Name)]
public class DeadLetterTests(PostgresFixture postgres, LocalStackFixture localStack) : IClassFixture<LocalStackFixture>
{
    [Fact]
    [Requirement("QR-6", "A message that cannot be processed is moved to a dead-letter queue and raises an alert.")]
    public async Task UnprocessableMessage_GoesToDeadLetterQueue()
    {
        var sqs = localStack.Sqs;
        var deadLetterUrl = (await sqs.CreateQueueAsync($"assessments-dlq-{Guid.NewGuid():N}")).QueueUrl;
        var deadLetterArn = (await sqs.GetQueueAttributesAsync(deadLetterUrl, ["QueueArn"])).QueueARN;
        var queueUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = $"assessments-{Guid.NewGuid():N}",
            Attributes = new Dictionary<string, string>
            {
                ["VisibilityTimeout"] = "1",
                ["RedrivePolicy"] = JsonSerializer.Serialize(new { deadLetterTargetArn = deadLetterArn, maxReceiveCount = "3" }),
            },
        })).QueueUrl;
        var queue = new SqsQueue(sqs, Options.Create(new QueueSettings
        {
            WaitSeconds = 2,
            Queues = { [Queues.Assessments] = new QueueUrl { Url = queueUrl } },
        }));

        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        s.Factory.Assessor.Respond = _ => throw new AssessmentFailedException("The model returned no valid result.");
        var runId = await s.StartRunAsync();
        await queue.SendAsync(Queues.Assessments, s.Factory.Queue.Assessments().Single(), CancellationToken.None);
        var consumer = new AssessmentConsumer(
            s.Factory.Services.GetRequiredService<IServiceScopeFactory>(), queue,
            Options.Create(new WorkerSettings()), NullLogger<AssessmentConsumer>.Instance);

        // Three receives fail. The third records the failure on the control.
        for (var receive = 1; receive <= 3; receive++)
        {
            Assert.Equal(1, await consumer.ProcessBatchAsync(CancellationToken.None));
        }

        var control = await s.Db.AssessmentRunControls.AsNoTracking().SingleAsync(rc => rc.RunId == runId);
        Assert.NotNull(control.FailedAt);
        Assert.Equal(RunStatus.Failed, (await s.RunAsync(runId)).Status);

        // The next receive finds nothing on the queue: SQS has moved the message to the dead-letter queue.
        Assert.Equal(0, await consumer.ProcessBatchAsync(CancellationToken.None));
        var dead = (await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = deadLetterUrl,
            WaitTimeSeconds = 5,
        })).Messages;
        Assert.Contains(runId.ToString(), Assert.Single(dead).Body, StringComparison.OrdinalIgnoreCase);
    }

    // The rest of QR-6: the alarm on a non-empty dead-letter queue is infrastructure (CDK). Fill in
    // with an infrastructure test, then fold it into the QR-6 test above.
    [Fact(Skip = "Pending: infrastructure")]
    public void DeadLetterQueue_RaisesAnAlert()
    {
    }
}
