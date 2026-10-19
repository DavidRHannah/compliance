using App.Data.Entities;

namespace App.Common.Access;

/// <summary>The signed-in user, as the backend knows them.</summary>
public sealed record Caller(Guid Id, UserRole Role, Guid? ClientId, bool IsDisabled);

/// <summary>The two engagement fields the access check needs.</summary>
public sealed record EngagementRef(Guid Id, Guid ClientId);

public enum AccessOutcome { Allowed, Unauthenticated, Disabled, NotFound, Forbidden }

/// <summary>
/// The order of the checks, with no I/O so it can be tested alone:
/// 401, 403 (disabled), 404 (engagement not visible), 403 (role).
/// </summary>
public static class AccessCheck
{
    public static AccessOutcome Decide(
        Caller? caller,
        EngagementRef? engagement,
        bool isEngagementRoute,
        AllowedRoles? roles)
    {
        if (caller is null)
        {
            return AccessOutcome.Unauthenticated;
        }

        if (caller.IsDisabled)
        {
            return AccessOutcome.Disabled;
        }

        // A client user sees only their own client's engagements. Leading an engagement
        // gives no access, so lead_consultant_id is not part of this check (EN-9).
        if (isEngagementRoute && !CanSee(caller, engagement))
        {
            return AccessOutcome.NotFound;
        }

        // A route that declares no roles is refused, so a forgotten declaration is never open.
        if (roles is null || !roles.Contains(caller.Role))
        {
            return AccessOutcome.Forbidden;
        }

        return AccessOutcome.Allowed;
    }

    private static bool CanSee(Caller caller, EngagementRef? engagement)
    {
        if (engagement is null)
        {
            return false;
        }
        // If the caller is an admin or consultant, they may access.
        // If the client company's id doesn't match the engagement's
        // client company id then it defers to User Role check above.
        return caller.Role != UserRole.Client || caller.ClientId == engagement.ClientId;
    }
}
