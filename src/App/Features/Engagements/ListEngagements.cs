using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Engagements;

/// <summary>Not an engagement route, so this handler applies the client rule itself (AC-5).</summary>
public static class ListEngagements
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    }

    private static async Task<IResult> Handle(
        string? status, [AsParameters] PageQuery page, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var errors = page.Validate();
        EngagementStatus? statusFilter = null;
        if (status is not null)
        {
            // By name only, so "1" is not read as a status.
            statusFilter = Enum.GetValues<EngagementStatus>()
                .Cast<EngagementStatus?>()
                .FirstOrDefault(v => string.Equals(v.ToString(), status, StringComparison.OrdinalIgnoreCase));
            if (statusFilter is null)
            {
                errors["status"] = ["Must be draft, active or closed."];
            }
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var query = db.Engagements.AsNoTracking();

        var caller = access.Caller;
        if (caller.Role == UserRole.Client)
        {
            query = query.Where(e => e.ClientId == caller.ClientId);
        }

        if (statusFilter is { } s)
        {
            query = query.Where(e => e.Status == s);
        }

        var result = await query.ToPageAsync(e => e.Id, EngagementResponse.Projection, e => e.Id, page, ct);
        return Results.Ok(result);
    }
}
