using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Users;

/// <summary>
/// Disables a user (AC-7). The backend refuses their requests at once, even with a valid token;
/// Cognito blocks sign-in and signs them out everywhere.
/// </summary>
public static class DisableUser
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/users/{userId:guid}/disable", Handle).AllowRoles(UserRole.Admin);
    }

    /// <summary>The refusal type, or null when the disable may go ahead.</summary>
    public static string? Check(Guid callerId, Guid targetId, bool targetIsEnabledAdmin, int enabledAdmins)
    {
        if (callerId == targetId)
        {
            return "cannot-disable-self";
        }

        return targetIsEnabledAdmin && enabledAdmins <= 1 ? "last-admin" : null;
    }

    private static async Task<IResult> Handle(
        Guid userId, AccessContext access, AppDbContext db, IUserDirectory directory, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Lock the enabled admins first, so two admins disabling each other cannot both succeed.
        var enabledAdmins = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM users WHERE role = 'admin' AND disabled_at IS NULL FOR UPDATE")
            .ToListAsync(ct);

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Problems.NotFound();
        }

        var refusal = Check(access.Caller.Id, user.Id, enabledAdmins.Contains(user.Id), enabledAdmins.Count);
        if (refusal is not null)
        {
            return Problems.Conflict(refusal);
        }

        if (user.DisabledAt is not null)
        {
            return Results.NoContent();
        }

        user.DisabledAt = DateTimeOffset.UtcNow;
        db.AddAudit(access.Caller.Id, null, "user.disabled", "user", user.Id);
        await db.SaveChangesAsync(ct);

        // Before commit: if Cognito fails, the database change rolls back too.
        await directory.DisableAsync(user.ExternalId, ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
