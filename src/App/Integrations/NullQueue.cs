using App.Integrations.Abstractions;

namespace App.Integrations;

/// <summary>
/// Development only, until the SQS queue exists: logs the message type and drops the message.
/// Outside development no queue is registered, so a send fails visibly.
/// </summary>
public sealed class NullQueue(ILogger<NullQueue> logger) : IQueue
{
    public Task SendAsync<TMessage>(string queue, TMessage message, CancellationToken ct)
    {
        logger.LogInformation("No queue configured; dropped {MessageType} for {Queue}", typeof(TMessage).Name, queue);
        return Task.CompletedTask;
    }
}
