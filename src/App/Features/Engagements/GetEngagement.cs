using App.Common.Access;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Engagements;

public static class GetEngagement
{
    public sealed record Response(
        Guid Id, Guid ClientId, Guid LeadConsultantId, string Name, EngagementStatus Status, DateTimeOffset CreatedAt);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    }

    private static async Task<Response> Handle(AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var engagementId = access.EngagementId;
        return await db.Engagements
            .AsNoTracking()
            .Where(e => e.Id == engagementId)
            .Select(e => new Response(e.Id, e.ClientId, e.LeadConsultantId, e.Name, e.Status, e.CreatedAt))
            .SingleAsync(ct);
    }
}
