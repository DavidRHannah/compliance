using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Engagements;

/// <summary>The engagement's controls with their applicability. A page of 100 holds all 93.</summary>
public static class ListEngagementControls
{
    public sealed record Response(
        Guid ControlId, string RefCode, string Theme, string Title,
        bool IsApplicable, string? Justification, DateTimeOffset? UpdatedAt);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/controls", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
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
        var result = await (
                from ec in db.EngagementControls.AsNoTracking()
                where ec.EngagementId == engagementId
                join c in db.Controls on ec.ControlId equals c.Id
                select new { ec, c })
            .ToPageAsync(
                x => x.ec.ControlId,
                x => new Response(x.c.Id, x.c.RefCode, x.c.Theme, x.c.Title, x.ec.IsApplicable, x.ec.Justification, x.ec.UpdatedAt),
                r => r.ControlId,
                page,
                ct);
        return Results.Ok(result);
    }
}
