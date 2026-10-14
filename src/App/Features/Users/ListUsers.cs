using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Users;

public static class ListUsers
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/users", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle([AsParameters] PageQuery page, AppDbContext db, CancellationToken ct)
    {
        var errors = page.Validate();
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var result = await db.Users.AsNoTracking()
            .ToPageAsync(u => u.Id, UserResponse.Projection, u => u.Id, page, ct);
        return Results.Ok(result);
    }
}
