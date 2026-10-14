using App.Common.Access;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Clients;

public static class GetClient
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/clients/{clientId:guid}", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(Guid clientId, AppDbContext db, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(ClientResponse.Projection)
            .SingleOrDefaultAsync(ct);
        return client is null ? Problems.NotFound() : Results.Ok(client);
    }
}
