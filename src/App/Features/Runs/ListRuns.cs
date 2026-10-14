using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Runs;

public static class ListRuns
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/runs", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(
        [AsParameters] PageQuery page, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var errors = page.Validate();
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var engagementId = access.EngagementId;
        var result = await db.AssessmentRuns.AsNoTracking()
            .Where(r => r.EngagementId == engagementId)
            .ToPageAsync(r => r.Id, RunResponse.Select(db), r => r.Id, page, ct);
        return Results.Ok(result);
    }
}
