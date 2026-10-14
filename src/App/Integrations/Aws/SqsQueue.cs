using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using App.Integrations.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Integrations.Aws;

public sealed class QueueSettings
{
    /// <summary>Queue name to its URL, for example assessments.</summary>
    public Dictionary<string, QueueUrl> Queues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long a receive waits for a message (long polling).</summary>
    public int WaitSeconds { get; set; } = 20;
}

public sealed class QueueUrl
{
    public string Url { get; set; } = "";
}

/// <summary>SQS. Bodies are JSON. A message that is not deleted comes back after its visibility timeout.</summary>
public sealed class SqsQueue(IAmazonSQS sqs, IOptions<QueueSettings> settings) : IQueue, IQueueReceiver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task SendAsync<TMessage>(string queue, TMessage message, CancellationToken ct) =>
        sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = UrlOf(queue),
            MessageBody = JsonSerializer.Serialize(message, Json),
        }, ct);

    public async Task<IReadOnlyList<ReceivedMessage<TMessage>>> ReceiveAsync<TMessage>(
        string queue, int max, CancellationToken ct)
    {
        var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = UrlOf(queue),
            MaxNumberOfMessages = max,
            WaitTimeSeconds = settings.Value.WaitSeconds,
            MessageSystemAttributeNames = ["ApproximateReceiveCount"],
        }, ct);

        return (response.Messages ?? [])
            .Select(m => new ReceivedMessage<TMessage>(
                JsonSerializer.Deserialize<TMessage>(m.Body, Json)!,
                m.ReceiptHandle,
                int.Parse(m.Attributes["ApproximateReceiveCount"])))
            .ToList();
    }

    public Task DeleteAsync(string queue, string receipt, CancellationToken ct) =>
        sqs.DeleteMessageAsync(UrlOf(queue), receipt, ct);

    private string UrlOf(string queue) =>
        settings.Value.Queues.TryGetValue(queue, out var q) && q.Url.Length > 0
            ? q.Url
            : throw new InvalidOperationException($"No URL is configured for queue '{queue}' (Queues:{queue}:Url).");
}
