using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.EngagementResults;

public enum ResultStatus { Pass, Fail, InsufficientEvidence, Pending, NotApplicable }

public sealed record ResultItem(Guid ControlId, string RefCode, string Theme, string Title, ResultStatus Status, bool UnderReview);

public sealed record ResultTotals(int Total, int Pass, int Fail, int InsufficientEvidence, int Pending, int NotApplicable, int UnderReview);

/// <summary>
/// Each control's latest consultant verdict, not applicable, or pending (RV-4, RV-13). This reads only
/// final verdicts and whether an assessment is current, never preliminary verdicts, rationales,
/// citations or notes, so client users may see it.
/// </summary>
public static class ResultsQuery
{
    private sealed record Decided(Guid ControlId, bool IsCurrent, FinalVerdict FinalVerdict, DateTimeOffset DecidedAt, Guid Id);

    /// <summary>All of the engagement's controls, ordered by control ID. At most 93 in phase one.</summary>
    public static async Task<List<ResultItem>> LoadAsync(AppDbContext db, Guid engagementId, CancellationToken ct)
    {
        var controls = await (
                from ec in db.EngagementControls.AsNoTracking()
                where ec.EngagementId == engagementId
                join c in db.Controls on ec.ControlId equals c.Id
                orderby c.Id
                select new { c.Id, c.RefCode, c.Theme, c.Title, ec.IsApplicable })
            .ToListAsync(ct);

        var decisions = (await (
                    from d in db.ConsultantDecisions.AsNoTracking()
                    join a in db.Assessments on d.AssessmentId equals a.Id
                    where a.EngagementId == engagementId
                    select new Decided(a.ControlId, a.IsCurrent, d.FinalVerdict, d.DecidedAt, d.Id))
                .ToListAsync(ct))
            .ToLookup(d => d.ControlId);

        return controls.Select(c =>
        {
            var all = decisions[c.Id].ToList();
            var latest = all.OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id).FirstOrDefault();
            var status = !c.IsApplicable ? ResultStatus.NotApplicable
                : latest is null ? ResultStatus.Pending
                : latest.FinalVerdict switch
                {
                    FinalVerdict.Pass => ResultStatus.Pass,
                    FinalVerdict.Fail => ResultStatus.Fail,
                    _ => ResultStatus.InsufficientEvidence,
                };

            // Under review: the latest decision is on an earlier assessment and the current one has none yet (RV-13).
            var underReview = c.IsApplicable && latest is not null && !all.Any(d => d.IsCurrent);
            return new ResultItem(c.Id, c.RefCode, c.Theme, c.Title, status, underReview);
        }).ToList();
    }

    public static ResultTotals Totals(IReadOnlyCollection<ResultItem> items) => new(
        items.Count,
        items.Count(i => i.Status == ResultStatus.Pass),
        items.Count(i => i.Status == ResultStatus.Fail),
        items.Count(i => i.Status == ResultStatus.InsufficientEvidence),
        items.Count(i => i.Status == ResultStatus.Pending),
        items.Count(i => i.Status == ResultStatus.NotApplicable),
        items.Count(i => i.UnderReview));
}
