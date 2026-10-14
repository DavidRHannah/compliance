using System.Net.Mail;
using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Users;

/// <summary>
/// The only way a user is created (AC-3). The Cognito account comes first, because the users
/// row needs its subject. If the row cannot be saved, the account is deleted (DECISIONS.md #17).
/// </summary>
public static class InviteUser
{
    public sealed record Request(string? Email, string? DisplayName, UserRole? Role, Guid? ClientId);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/users", Handle).AllowRoles(UserRole.Admin);
    }

    private static async Task<IResult> Handle(
        Request request, AccessContext access, AppDbContext db, IUserDirectory directory, CancellationToken ct)
    {
        var email = request.Email?.Trim();
        var displayName = request.DisplayName?.Trim();

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(email) || !MailAddress.TryCreate(email, out _))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrEmpty(displayName))
        {
            errors["displayName"] = ["Required."];
        }

        if (request.Role is null)
        {
            errors["role"] = ["Required."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        // A client user belongs to exactly one client; staff belong to none.
        var role = request.Role!.Value;
        if ((role == UserRole.Client) != (request.ClientId is not null))
        {
            return Problems.BadRequest("role-client-mismatch", "A client user needs a client ID; other roles must not have one.");
        }

        if (request.ClientId is { } clientId && !await db.Clients.AnyAsync(c => c.Id == clientId, ct))
        {
            return Problems.BadRequest("invalid-reference", "The client does not exist.");
        }

        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email!.ToLower(), ct))
        {
            return Problems.Conflict("email-taken");
        }

        var subject = await directory.InviteAsync(email!, ct);
        var user = new User
        {
            ExternalId = subject,
            Email = email!,
            DisplayName = displayName!,
            Role = role,
            ClientId = request.ClientId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.AddAudit(access.Caller.Id, null, "user.invited", "user", user.Id, new { role, clientId = request.ClientId });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Never leave an account that has no users row.
            await directory.DeleteAsync(subject, CancellationToken.None);
            if (Problems.IsUniqueViolation(ex, "ix_users_email_lower"))
            {
                return Problems.Conflict("email-taken");
            }

            throw;
        }

        return Results.Created($"/api/users/{user.Id}", UserResponse.From(user));
    }
}
