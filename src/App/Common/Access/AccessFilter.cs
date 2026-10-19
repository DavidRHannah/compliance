using App.Common.Errors;
using App.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Common.Access;

/// <summary>
/// Runs before every handler under /api. The token is already valid (401 otherwise).
/// This loads the caller and the engagement, applies <see cref="AccessCheck"/>, and
/// turns a failure into problem details.
/// </summary>
public sealed class AccessFilter(AppDbContext db, AccessContext access) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;

        var subject = http.User.FindFirst("sub")?.Value;
        var caller = subject is null
            ? null
            : await db.Users
                .AsNoTracking()
                .Where(u => u.ExternalId == subject)
                .Select(u => new Caller(u.Id, u.Role, u.ClientId, u.DisabledAt != null))
                .SingleOrDefaultAsync(ct);

        // An engagement route has {engagementId:guid} in its template (the spec's "…/").
        var isEngagementRoute = http.GetRouteData().Values.TryGetValue("engagementId", out var raw);
        EngagementRef? engagement = null;
        if (isEngagementRoute && Guid.TryParse(raw?.ToString(), out var engagementId))
        {
            engagement = await db.Engagements
                .AsNoTracking()
                .Where(e => e.Id == engagementId)
                .Select(e => new EngagementRef(e.Id, e.ClientId))
                .SingleOrDefaultAsync(ct);
        }

        var roles = http.GetEndpoint()?.Metadata.GetMetadata<AllowedRoles>();
        var outcome = AccessCheck.Decide(caller, engagement, isEngagementRoute, roles);

        if (outcome != AccessOutcome.Allowed)
        {
            return Refuse(outcome);
        }

        // caller! is safe because a null caller is refused by AccessCheck.Decide
        access.Set(caller!, engagement?.Id);
        return await next(context);
    }

    private static IResult Refuse(AccessOutcome outcome) => outcome switch
    {
        AccessOutcome.Unauthenticated => Problems.Problem(StatusCodes.Status401Unauthorized, "unauthenticated", "Unauthenticated"),
        AccessOutcome.Disabled => Problems.Problem(StatusCodes.Status403Forbidden, "user-disabled", "User disabled"),
        AccessOutcome.NotFound => Problems.NotFound(),
        _ => Problems.Forbidden(),
    };
}

/// <summary>Endpoint metadata: the route runs behind <see cref="AccessFilter"/>. A test requires it on every /api route.</summary>
public sealed class AccessChecked
{
    public static readonly AccessChecked Instance = new();

    private AccessChecked()
    {
    }
}

public static class AccessFilterExtensions
{
    /// <summary>Adds the access filter and the marker together, so neither is ever added alone.</summary>
    public static RouteGroupBuilder WithAccessCheck(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<AccessFilter>();
        return group.WithMetadata(AccessChecked.Instance);
    }
}
