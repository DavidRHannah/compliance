using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Clients;

public static class ListClients
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/clients", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle([AsParameters] PageQuery page, AppDbContext db, CancellationToken ct)
    {
        var errors = page.Validate();
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var result = await db.Clients.AsNoTracking()
            .ToPageAsync(c => c.Id, ClientResponse.Projection, c => c.Id, page, ct);
        return Results.Ok(result);
    }
}
