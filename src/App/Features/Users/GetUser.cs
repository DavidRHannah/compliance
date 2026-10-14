using App.Common.Access;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Users;

public static class GetUser
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/users/{userId:guid}", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(Guid userId, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(UserResponse.Projection)
            .SingleOrDefaultAsync(ct);
        return user is null ? Problems.NotFound() : Results.Ok(user);
    }
}
