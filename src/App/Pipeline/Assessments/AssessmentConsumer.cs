using App.Integrations.Abstractions;
using App.Pipeline.Assessments;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

/// <summary>
/// Reads the assessment queue and assesses up to <see cref="WorkerSettings.Concurrency"/> controls at once.
/// A message that fails stays on the queue and comes back. On its third receive the failure is recorded
/// on the control, and SQS then moves the message to the dead-letter queue (QR-6).
/// </summary>
public sealed class AssessmentConsumer(
    IServiceScopeFactory scopes, IQueueReceiver queue, IOptions<WorkerSettings> settings, ILogger<AssessmentConsumer> logger)
    : BackgroundService
{
    public const int MaxReceives = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // Reading the queue failed. The type is enough; wait, then try again.
                logger.LogError("Receiving from the assessment queue failed: {ErrorType}", e.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    /// <summary>Receives one batch and handles it. Returns the number of messages received.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var size = Math.Clamp(settings.Value.Concurrency, 1, 10);
        var messages = await queue.ReceiveAsync<AssessControlMessage>(Queues.Assessments, size, ct);
        await Task.WhenAll(messages.Select(m => ProcessAsync(m, ct)));
        return messages.Count;
    }

    private async Task ProcessAsync(ReceivedMessage<AssessControlMessage> received, CancellationToken ct)
    {
        var message = received.Body;
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = message.CorrelationId,
            ["RunId"] = message.RunId,
            ["ControlId"] = message.ControlId,
        });

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AssessControl>().HandleAsync(message, ct);
            await queue.DeleteAsync(Queues.Assessments, received.Receipt, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            var error = Describe(e);
            logger.LogWarning("Assessing a control failed on receive {ReceiveCount}: {Error}", received.ReceiveCount, error);
            if (received.ReceiveCount >= MaxReceives)
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AssessmentWriter>()
                    .RecordFailureAsync(message.RunId, message.EngagementId, message.ControlId, error, ct);
            }
        }
    }

    /// <summary>
    /// Text safe to store and log. Our own failure messages hold no document text; any other exception
    /// is reduced to its type, because its message could hold values from the database or the model.
    /// </summary>
    public static string Describe(Exception e) =>
        e is AssessmentFailedException ? e.Message : e.GetType().Name;
}
