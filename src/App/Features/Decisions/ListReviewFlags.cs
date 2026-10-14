using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Decisions;

/// <summary>Open flags on current assessments (RV-7, RV-8).</summary>
public static class ListReviewFlags
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/review-flags", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(
        string? reason, [AsParameters] PageQuery page, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var errors = page.Validate();
        var reasonFilter = EnumQuery.Parse<ReviewFlagReason>(reason);
        if (reason is not null && reasonFilter is null)
        {
            errors["reason"] = [$"Must be one of: {EnumQuery.Names<ReviewFlagReason>()}."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var engagementId = access.EngagementId;
        var open = db.ReviewFlags.AsNoTracking().Where(f => f.EngagementId == engagementId && f.ClearedAt == null);
        if (reasonFilter is { } r)
        {
            open = open.Where(f => f.Reason == r);
        }

        var query =
            from f in open
            join a in db.Assessments on f.AssessmentId equals a.Id
            where a.EngagementId == engagementId && a.IsCurrent
            join c in db.Controls on a.ControlId equals c.Id
            select new { f, a, c };
        var rows = await query.ToPageAsync(
            x => x.f.Id, x => new FlagJoin(x.f, x.a.IsCurrent, x.c), j => j.Flag.Id, page, ct);
        var items = await FlagQueries.ToResponsesAsync(db, engagementId, rows.Items, ct);
        return Results.Ok(new Page<FlagResponse>(items, rows.NextCursor));
    }
}
