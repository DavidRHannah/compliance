using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Decisions;

/// <summary>
/// A consultant clears a flag without recording a decision (RV-12). It changes no verdict and starts
/// no run (RV-9). Clearing a flag that is already cleared is harmless.
/// </summary>
public static class ClearReviewFlag
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/engagements/{engagementId:guid}/review-flags/{flagId:guid}/clear", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(Guid flagId, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var engagementId = access.EngagementId;
        var (transaction, status) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Share, ct);
        await using var _ = transaction;

        var flag = await db.ReviewFlags.SingleOrDefaultAsync(f => f.Id == flagId && f.EngagementId == engagementId, ct);
        if (flag is null)
        {
            return Problems.NotFound();
        }

        if (status == EngagementStatus.Closed)
        {
            return Problems.Conflict("engagement-closed");
        }

        if (flag.ClearedAt is null)
        {
            flag.ClearedAt = DateTimeOffset.UtcNow;
            flag.ClearedById = access.Caller.Id;
            db.AddAudit(access.Caller.Id, engagementId, "review_flag.cleared", "review_flag", flag.Id,
                new { assessmentId = flag.AssessmentId, reason = flag.Reason });
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
