using App.Common.Access;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using App.Features.Decisions;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Assessments;

/// <summary>
/// One assessment, current or not, with its rationale, verified citations, decisions and open flags
/// (RV-1, RV-5, RV-7, AS-6). A flag for a superseded decision carries the earlier decision.
/// </summary>
public static class GetAssessment
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/assessments/{assessmentId:guid}", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(Guid assessmentId, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var engagementId = access.EngagementId;
        var row = await (
                from a in db.Assessments.AsNoTracking()
                where a.Id == assessmentId && a.EngagementId == engagementId
                join c in db.Controls on a.ControlId equals c.Id
                select new { a, c })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return Problems.NotFound();
        }

        var citations = await AssessmentQueries.VerifiedCitationsAsync(db, engagementId, [assessmentId], ct);
        var decisions = (await DecisionQueries.Rows(db, engagementId, [assessmentId]).ToListAsync(ct))
            .OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id)
            .Select(d => d.ToResponse()).ToList();
        var joins = (await FlagQueries.Joined(db, engagementId, f => f.AssessmentId == assessmentId && f.ClearedAt == null)
                .ToListAsync(ct))
            .OrderBy(j => j.Flag.Id).ToList();
        var flags = await FlagQueries.ToResponsesAsync(db, engagementId, joins, ct);

        return Results.Ok(new AssessmentDetail(
            row.a.Id, row.c.Id, row.c.RefCode, row.c.Title, row.a.RunId, row.a.Verdict, row.a.Rationale,
            row.a.IsCurrent, row.a.CreatedAt, citations, decisions, flags));
    }
}
