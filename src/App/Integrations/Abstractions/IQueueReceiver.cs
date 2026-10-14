namespace App.Integrations.Abstractions;

/// <summary>A message with the receipt needed to delete it and the number of times it was delivered.</summary>
public sealed record ReceivedMessage<TMessage>(TMessage Body, string Receipt, int ReceiveCount);

/// <summary>Reads work from a queue. A message not deleted comes back, and after three receives goes to the dead-letter queue.</summary>
public interface IQueueReceiver
{
    Task<IReadOnlyList<ReceivedMessage<TMessage>>> ReceiveAsync<TMessage>(string queue, int max, CancellationToken ct);

    Task DeleteAsync(string queue, string receipt, CancellationToken ct);
}
