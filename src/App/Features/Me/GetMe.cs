using App.Common.Access;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Me;

public static class GetMe
{
    public sealed record Response(Guid Id, string Email, string DisplayName, UserRole Role, Guid? ClientId);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/me", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    }

    private static async Task<Response> Handle(AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var id = access.Caller.Id;
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new Response(u.Id, u.Email, u.DisplayName, u.Role, u.ClientId))
            .SingleAsync(ct);
    }
}
