using App.Data.Entities;

namespace App.Integrations.Abstractions;

/// <summary>Asks the language model for a preliminary verdict on one control.</summary>
public interface IControlAssessor
{
    /// <summary>The model identifier, recorded on each run (AS-8).</summary>
    string ModelId { get; }

    /// <summary>The prompt template version, recorded on each run (AS-8).</summary>
    string PromptVersion { get; }

    /// <summary>Throws <see cref="AssessmentFailedException"/> when the model gives no usable result.</summary>
    Task<AssessorResult> AssessAsync(ControlContext context, CancellationToken ct);
}

/// <summary>Everything the prompt is built from: the three scopes (AS-11).</summary>
public sealed record ControlContext(
    ControlText Control,
    CriteriaText Criteria,
    IReadOnlyList<ScopeItemText> ScopeItems,
    IReadOnlyList<EvidenceChunk> Evidence);

public sealed record ControlText(string RefCode, string Title, string Statement);

public sealed record CriteriaText(
    IReadOnlyList<string> ExpectedEvidence, string PassConditions, string FailConditions, bool RequiresOperationEvidence);

public sealed record ScopeItemText(ScopeItemType Type, string Name, string? Description);

/// <summary>A chunk with its document's name, type and upload date (AS-12).</summary>
public sealed record EvidenceChunk(
    Guid ChunkId, string Content, string? SectionTitle, string DocumentName, string MimeType, DateTimeOffset UploadedAt);

public sealed record AssessorResult(PreliminaryVerdict Verdict, string Rationale, IReadOnlyList<ModelCitation> Citations);

/// <summary>A citation as the model gave it: a chunk ID, not yet checked, and a quote.</summary>
public sealed record ModelCitation(string ChunkId, string Quote);

/// <summary>The message is safe to store and log: it never holds document text, quotes or prompts.</summary>
public sealed class AssessmentFailedException(string message) : Exception(message);
