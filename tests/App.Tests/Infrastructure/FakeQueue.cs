using System.Collections.Concurrent;
using App.Integrations.Abstractions;

namespace App.Tests.Infrastructure;

/// <summary>Stands in for SQS: keeps every message sent.</summary>
public sealed class FakeQueue : IQueue
{
    public ConcurrentQueue<(string Queue, object Message)> Sent { get; } = new();

    public Task SendAsync<TMessage>(string queue, TMessage message, CancellationToken ct)
    {
        Sent.Enqueue((queue, message!));
        return Task.CompletedTask;
    }

    /// <summary>The assessment messages sent so far, oldest first.</summary>
    public List<AssessControlMessage> Assessments() =>
        Sent.Where(s => s.Queue == Queues.Assessments).Select(s => (AssessControlMessage)s.Message).ToList();
}
