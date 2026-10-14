namespace App.Integrations.Abstractions;

/// <summary>Sends work to a queue. Handlers must be safe to run more than once (QR-5).</summary>
public interface IQueue
{
    Task SendAsync<TMessage>(string queue, TMessage message, CancellationToken ct);
}

public static class Queues
{
    /// <summary>Document processing and scope re-linking.</summary>
    public const string Documents = "documents";

    /// <summary>One message per control of a run.</summary>
    public const string Assessments = "assessments";
}

/// <summary>Assess one control in a run. The correlation ID links the logs of the request and the job (QR-8).</summary>
public sealed record AssessControlMessage(Guid RunId, Guid EngagementId, Guid ControlId, string CorrelationId);

/// <summary>Scope items changed: run the link step again for the engagement's documents.</summary>
public sealed record RelinkScopeMessage(Guid EngagementId);
