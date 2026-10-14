using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;
using App.Features.Decisions;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Assessments;

/// <summary>
/// Every assessment of one control, oldest first like other lists, with the run, model, prompt version
/// and criteria version behind each verdict (QR-7).
/// </summary>
public static class ListControlAssessments
{
    public sealed record HistoryItem(
        Guid Id, Guid RunId, string ModelId, string PromptVersion, Guid CriteriaId, int CriteriaVersion,
        PreliminaryVerdict Verdict, bool IsCurrent, DateTimeOffset CreatedAt, DecisionResponse? Decision);

    private sealed record HistoryRow(
        Guid Id, Guid RunId, string ModelId, string PromptVersion, Guid CriteriaId, int CriteriaVersion,
        PreliminaryVerdict Verdict, bool IsCurrent, DateTimeOffset CreatedAt);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/controls/{controlId:guid}/assessments", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(
        Guid controlId, [AsParameters] PageQuery page, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var errors = page.Validate();
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var engagementId = access.EngagementId;
        if (!await db.EngagementControls.AnyAsync(ec => ec.EngagementId == engagementId && ec.ControlId == controlId, ct))
        {
            return Problems.NotFound();
        }

        var query =
            from a in db.Assessments.AsNoTracking()
            where a.EngagementId == engagementId && a.ControlId == controlId
            join r in db.AssessmentRuns on new { Id = a.RunId, a.EngagementId } equals new { r.Id, r.EngagementId }
            join rc in db.AssessmentRunControls on new { a.RunId, a.ControlId } equals new { rc.RunId, rc.ControlId }
            join k in db.AssessmentCriteria on rc.AssessmentCriteriaId equals k.Id
            select new { a, r, k };

        var result = await query.ToPageAsync(
            x => x.a.Id,
            x => new HistoryRow(x.a.Id, x.r.Id, x.r.ModelId, x.r.PromptVersion, x.k.Id, x.k.Version, x.a.Verdict, x.a.IsCurrent, x.a.CreatedAt),
            r => r.Id,
            page,
            ct);

        var ids = result.Items.Select(i => i.Id).ToArray();
        var decisions = (await DecisionQueries.OfRecord(db, engagementId, ids).ToListAsync(ct))
            .ToDictionary(d => d.AssessmentId, d => d.ToResponse());
        var items = result.Items.Select(r => new HistoryItem(
            r.Id, r.RunId, r.ModelId, r.PromptVersion, r.CriteriaId, r.CriteriaVersion, r.Verdict, r.IsCurrent, r.CreatedAt,
            decisions.GetValueOrDefault(r.Id))).ToList();
        return Results.Ok(new Page<HistoryItem>(items, result.NextCursor));
    }
}
