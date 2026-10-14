using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;

namespace App.Features.Clients;

public static class CreateClient
{
    public sealed record Request(string? Name);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/clients", Handle).AllowRoles(UserRole.Admin);
    }

    private static async Task<IResult> Handle(Request request, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["name"] = ["Required."] });
        }

        var client = new Client { Name = name, CreatedAt = DateTimeOffset.UtcNow };
        db.Clients.Add(client);
        db.AddAudit(access.Caller.Id, null, "client.created", "client", client.Id, new { name });
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/clients/{client.Id}", ClientResponse.From(client));
    }
}
