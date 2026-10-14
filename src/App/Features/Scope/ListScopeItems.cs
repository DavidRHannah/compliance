using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Scope;

public static class ListScopeItems
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/scope-items", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
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
        var result = await db.ScopeItems.AsNoTracking()
            .Where(s => s.EngagementId == engagementId)
            .ToPageAsync(s => s.Id, ScopeItemResponse.Projection, s => s.Id, page, ct);
        return Results.Ok(result);
    }
}
