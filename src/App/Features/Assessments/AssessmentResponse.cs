using System.Text.Json;
using App.Data;
using App.Data.Entities;
using App.Features.Decisions;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Assessments;

/// <summary>An assessment with its control, latest decision and open-flag count.</summary>
public sealed record AssessmentSummary(
    Guid Id, Guid ControlId, string RefCode, string Title, Guid RunId, PreliminaryVerdict Verdict,
    bool IsCurrent, DateTimeOffset CreatedAt, int OpenFlags, DecisionResponse? Decision);

public sealed record AssessmentDetail(
    Guid Id, Guid ControlId, string RefCode, string Title, Guid RunId, PreliminaryVerdict Verdict, string Rationale,
    bool IsCurrent, DateTimeOffset CreatedAt, IReadOnlyList<CitationResponse> Citations,
    IReadOnlyList<DecisionResponse> Decisions, IReadOnlyList<FlagResponse> Flags);

/// <summary>A verified citation and where it came from (RV-1). Unverified citations are never returned (AS-6).</summary>
public sealed record CitationResponse(
    Guid Id, Guid AssessmentId, Guid ChunkId, Guid DocumentId, string DocumentName, string? SectionTitle,
    JsonElement Locator, string QuotedText, int CharStart, int CharEnd);

/// <summary>An assessment row before its decision is attached.</summary>
public sealed record SummaryRow(
    Guid Id, Guid ControlId, string RefCode, string Title, Guid RunId, PreliminaryVerdict Verdict,
    bool IsCurrent, DateTimeOffset CreatedAt, int OpenFlags);

public static class AssessmentQueries
{
    /// <summary>Attaches each assessment's decision of record.</summary>
    public static async Task<List<AssessmentSummary>> WithDecisionsAsync(
        AppDbContext db, Guid engagementId, IReadOnlyList<SummaryRow> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToArray();
        var decisions = (await DecisionQueries.OfRecord(db, engagementId, ids).ToListAsync(ct))
            .ToDictionary(d => d.AssessmentId, d => d.ToResponse());
        return rows.Select(r => new AssessmentSummary(
            r.Id, r.ControlId, r.RefCode, r.Title, r.RunId, r.Verdict, r.IsCurrent, r.CreatedAt, r.OpenFlags,
            decisions.GetValueOrDefault(r.Id))).ToList();
    }

    /// <summary>The verified citations of the assessments, with their document and location (AS-6, RV-1).</summary>
    public static async Task<List<CitationResponse>> VerifiedCitationsAsync(
        AppDbContext db, Guid engagementId, IReadOnlyCollection<Guid> assessmentIds, CancellationToken ct)
    {
        var ids = assessmentIds.ToArray();
        var rows = await (
                from c in db.EvidenceCitations.AsNoTracking()
                where c.EngagementId == engagementId && c.IsVerified && ids.Contains(c.AssessmentId)
                join k in db.DocumentChunks on c.ChunkId equals k.Id
                join d in db.Documents on k.DocumentId equals d.Id
                orderby c.Id
                select new { c, k.Locator, k.SectionTitle, DocumentId = d.Id, d.FileName })
            .ToListAsync(ct);
        return rows.Select(r => new CitationResponse(
            r.c.Id, r.c.AssessmentId, r.c.ChunkId, r.DocumentId, r.FileName, r.SectionTitle,
            r.Locator.RootElement.Clone(), r.c.QuotedText, r.c.CharStart ?? 0, r.c.CharEnd ?? 0)).ToList();
    }
}
