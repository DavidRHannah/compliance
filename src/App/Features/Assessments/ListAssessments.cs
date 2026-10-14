using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Assessments;

/// <summary>The engagement's current assessments, with their latest decision and open-flag count (RV-1).</summary>
public static class ListAssessments
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/assessments", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(
        string? verdict, bool? flagged, bool? decided, [AsParameters] PageQuery page,
        AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var errors = page.Validate();
        var verdictFilter = EnumQuery.Parse<PreliminaryVerdict>(verdict);
        if (verdict is not null && verdictFilter is null)
        {
            errors["verdict"] = [$"Must be one of: {EnumQuery.Names<PreliminaryVerdict>()}."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var engagementId = access.EngagementId;
        var query =
            from a in db.Assessments.AsNoTracking()
            where a.EngagementId == engagementId && a.IsCurrent
            join c in db.Controls on a.ControlId equals c.Id
            select new { a, c };

        if (verdictFilter is { } v)
        {
            query = query.Where(x => x.a.Verdict == v);
        }

        if (flagged is { } f)
        {
            query = query.Where(x => db.ReviewFlags.Any(r => r.AssessmentId == x.a.Id && r.ClearedAt == null) == f);
        }

        if (decided is { } d)
        {
            query = query.Where(x => db.ConsultantDecisions.Any(r => r.AssessmentId == x.a.Id) == d);
        }

        var result = await query.ToPageAsync(
            x => x.a.Id,
            x => new SummaryRow(
                x.a.Id, x.c.Id, x.c.RefCode, x.c.Title, x.a.RunId, x.a.Verdict, x.a.IsCurrent, x.a.CreatedAt,
                db.ReviewFlags.Count(r => r.AssessmentId == x.a.Id && r.ClearedAt == null)),
            r => r.Id,
            page,
            ct);
        var items = await AssessmentQueries.WithDecisionsAsync(db, engagementId, result.Items, ct);
        return Results.Ok(new Page<AssessmentSummary>(items, result.NextCursor));
    }
}
