using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Engagements;

/// <summary>
/// Draft to active to closed, never back (EN-2). Both lock the engagement FOR UPDATE, so a close
/// waits for writes holding FOR SHARE, and no write starts until the close commits.
/// </summary>
public static class ChangeEngagementStatus
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/engagements/{engagementId:guid}/activate", Activate).AllowRoles(UserRole.Admin, UserRole.Consultant);
        api.MapPost("/engagements/{engagementId:guid}/close", Close).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    /// <summary>The status a move leads to, or null when the move is not allowed.</summary>
    public static EngagementStatus? Next(EngagementStatus from, EngagementStatus to) =>
        (from, to) switch
        {
            (EngagementStatus.Draft, EngagementStatus.Active) => to,
            (EngagementStatus.Active, EngagementStatus.Closed) => to,
            _ => null,
        };

    private static Task<IResult> Activate(AccessContext access, AppDbContext db, CancellationToken ct) =>
        MoveAsync(EngagementStatus.Active, "engagement.activated", access, db, ct);

    private static Task<IResult> Close(AccessContext access, AppDbContext db, CancellationToken ct) =>
        MoveAsync(EngagementStatus.Closed, "engagement.closed", access, db, ct);

    private static async Task<IResult> MoveAsync(
        EngagementStatus to, string action, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var engagementId = access.EngagementId;
        var (transaction, status) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Update, ct);
        await using var _ = transaction;

        if (Next(status, to) is null)
        {
            return Problems.Conflict("invalid-transition", $"An engagement cannot move from {status} to {to}.");
        }

        if (to == EngagementStatus.Closed && await db.AssessmentRuns.AnyAsync(r =>
                r.EngagementId == engagementId && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running), ct))
        {
            return Problems.Conflict("run-active");
        }

        var engagement = await db.Engagements.SingleAsync(e => e.Id == engagementId, ct);
        engagement.Status = to;
        db.AddAudit(access.Caller.Id, engagementId, action, "engagement", engagementId, new { from = status, to });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
