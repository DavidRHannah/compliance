using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Decisions;

public sealed record FlagResponse(
    Guid Id, Guid AssessmentId, Guid ControlId, string RefCode, string Title, ReviewFlagReason Reason,
    Guid? DocumentId, string? DocumentName, Guid? SupersededAssessmentId, DateTimeOffset CreatedAt,
    DecisionResponse? EarlierDecision);

/// <summary>An open or cleared flag with its assessment and control.</summary>
public sealed record FlagJoin(ReviewFlag Flag, bool AssessmentIsCurrent, Control Control);

public static class FlagQueries
{
    /// <summary>The engagement's flags that match the filter. The filter runs on the flags, before the projection.</summary>
    public static IQueryable<FlagJoin> Joined(
        AppDbContext db, Guid engagementId, System.Linq.Expressions.Expression<Func<ReviewFlag, bool>> filter) =>
        from f in db.ReviewFlags.AsNoTracking().Where(filter)
        where f.EngagementId == engagementId
        join a in db.Assessments on f.AssessmentId equals a.Id
        where a.EngagementId == engagementId
        join c in db.Controls on a.ControlId equals c.Id
        select new FlagJoin(f, a.IsCurrent, c);

    /// <summary>Adds the document name and, for a superseded decision, the earlier decision (RV-7, RV-8).</summary>
    public static async Task<List<FlagResponse>> ToResponsesAsync(
        AppDbContext db, Guid engagementId, IReadOnlyList<FlagJoin> flags, CancellationToken ct)
    {
        var documentIds = flags.Where(f => f.Flag.DocumentId is not null).Select(f => f.Flag.DocumentId!.Value).Distinct().ToArray();
        var names = await db.Documents.AsNoTracking()
            .Where(d => d.EngagementId == engagementId && documentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.FileName, ct);

        var earlierIds = flags.Where(f => f.Flag.SupersededAssessmentId is not null).Select(f => f.Flag.SupersededAssessmentId!.Value).Distinct().ToArray();
        var earlier = (await DecisionQueries.OfRecord(db, engagementId, earlierIds).ToListAsync(ct))
            .ToDictionary(r => r.AssessmentId, r => r.ToResponse());

        return flags.Select(j => new FlagResponse(
            j.Flag.Id, j.Flag.AssessmentId, j.Control.Id, j.Control.RefCode, j.Control.Title, j.Flag.Reason,
            j.Flag.DocumentId, j.Flag.DocumentId is { } d ? names.GetValueOrDefault(d) : null,
            j.Flag.SupersededAssessmentId, j.Flag.CreatedAt,
            j.Flag.SupersededAssessmentId is { } s ? earlier.GetValueOrDefault(s) : null)).ToList();
    }
}
