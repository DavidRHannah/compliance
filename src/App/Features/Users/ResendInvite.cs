using App.Common.Access;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Users;

public static class ResendInvite
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/users/{userId:guid}/resend-invite", Handle).AllowRoles(UserRole.Admin);
    }

    private static async Task<IResult> Handle(Guid userId, AppDbContext db, IUserDirectory directory, CancellationToken ct)
    {
        var subject = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ExternalId)
            .SingleOrDefaultAsync(ct);
        if (subject is null)
        {
            return Problems.NotFound();
        }

        return await directory.ResendInviteAsync(subject, ct)
            ? Results.NoContent()
            : Problems.Conflict("invite-accepted");
    }
}
