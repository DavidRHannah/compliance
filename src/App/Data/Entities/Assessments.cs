using System.Text.Json;

namespace App.Data.Entities;

public class AssessmentRun
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }

    /// <summary>Consultant or admin.</summary>
    public Guid TriggeredById { get; set; }

    /// <summary>Bedrock model identifier.</summary>
    public required string ModelId { get; set; }

    public required string PromptVersion { get; set; }
    public RunStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One row per control a run queues, with the criteria version active at the start.</summary>
public class AssessmentRunControl
{
    public Guid RunId { get; set; }
    public Guid ControlId { get; set; }
    public Guid AssessmentCriteriaId { get; set; }

    /// <summary>Set after the last retry fails.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    public string? Error { get; set; }
}

/// <summary>Append-only apart from IsCurrent.</summary>
public class Assessment
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid EngagementId { get; set; }
    public Guid ControlId { get; set; }

    /// <summary>Always null in phase one.</summary>
    public Guid? ScopeItemId { get; set; }

    public PreliminaryVerdict Verdict { get; set; }
    public required string Rationale { get; set; }
    public bool IsCurrent { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Append-only.</summary>
public class EvidenceCitation
{
    public Guid Id { get; set; }
    public Guid AssessmentId { get; set; }
    public Guid ChunkId { get; set; }
    public Guid EngagementId { get; set; }
    public required string QuotedText { get; set; }

    /// <summary>Character offset in the chunk's content; null if unverified.</summary>
    public int? CharStart { get; set; }

    public int? CharEnd { get; set; }

    /// <summary>True when the quote was found in the chunk.</summary>
    public bool IsVerified { get; set; }
}

public class ReviewFlag
{
    public Guid Id { get; set; }
    public Guid AssessmentId { get; set; }
    public Guid EngagementId { get; set; }
    public ReviewFlagReason Reason { get; set; }

    /// <summary>The deleted or replaced document.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>The earlier assessment that had the decision.</summary>
    public Guid? SupersededAssessmentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when a consultant has re-reviewed.</summary>
    public DateTimeOffset? ClearedAt { get; set; }

    public Guid? ClearedById { get; set; }
}

/// <summary>Append-only. The decision of record is the latest by (DecidedAt, Id).</summary>
public class ConsultantDecision
{
    public Guid Id { get; set; }
    public Guid AssessmentId { get; set; }

    /// <summary>Consultant or admin.</summary>
    public Guid DecidedById { get; set; }

    public FinalVerdict FinalVerdict { get; set; }

    /// <summary>Required for an override.</summary>
    public string? Notes { get; set; }

    public DateTimeOffset DecidedAt { get; set; }
}

/// <summary>Append-only record of every change (RV-6). EngagementId is null for firm-level actions.</summary>
public class AuditEvent
{
    public Guid Id { get; set; }
    public Guid? EngagementId { get; set; }
    public Guid ActorId { get; set; }

    /// <summary>For example scope_item.updated, applicability.set, document.deleted.</summary>
    public required string Action { get; set; }

    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }

    /// <summary>Changed fields, old and new values.</summary>
    public JsonDocument? Details { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
