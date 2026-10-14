using App.Common.Access;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Engagements;

public static class GetEngagement
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    }

    private static async Task<EngagementResponse> Handle(AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var engagementId = access.EngagementId;
        return await db.Engagements
            .AsNoTracking()
            .Where(e => e.Id == engagementId)
            .Select(EngagementResponse.Projection)
            .SingleAsync(ct);
    }
}
