using App.Common.Access;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Me;

public static class GetMe
{
    // The response is a record type that represents the information about the current user.
    public sealed record Response(Guid Id, string Email, string DisplayName, UserRole Role, Guid? ClientId);

    // The Map method sets up the API endpoint for retrieving the current user's information.
    public static void Map(RouteGroupBuilder api)
    {
        // Get the current user's information.
        api.MapGet("/me", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    }

    // The Handle method retrieves the current user's information from the database and returns it as a Response object.
    private static async Task<Response> Handle(AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var id = access.Caller.Id; // Get the ID of the currently authenticated user from the access context
        
        // Query the database for the user with the specified ID, without tracking changes to the entity
        return await db.Users
            .AsNoTracking() // Do not track changes to the returned entities
            .Where(u => u.Id == id) // Filter to the user with the specified ID
            .Select(u => new Response(u.Id, u.Email, u.DisplayName, u.Role, u.ClientId)) // Project to the Response record
            .SingleAsync(ct);// Ensure that exactly one user is returned, otherwise throw an exception.
    }
}
