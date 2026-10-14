using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Users;

public static class EnableUser
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/users/{userId:guid}/enable", Handle).AllowRoles(UserRole.Admin);
    }

    private static async Task<IResult> Handle(
        Guid userId, AccessContext access, AppDbContext db, IUserDirectory directory, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Problems.NotFound();
        }

        if (user.DisabledAt is null)
        {
            return Results.NoContent();
        }

        user.DisabledAt = null;
        db.AddAudit(access.Caller.Id, null, "user.enabled", "user", user.Id);
        await db.SaveChangesAsync(ct);

        // Before commit: if Cognito fails, the database change rolls back too.
        await directory.EnableAsync(user.ExternalId, ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
