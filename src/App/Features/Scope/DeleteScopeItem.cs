using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Scope;

/// <summary>
/// Hard delete; the database deletes the item's mentions with it. An item an assessment refers to
/// cannot be deleted (EN-6). In phase one assessments never refer to one, so this never blocks yet.
/// </summary>
public static class DeleteScopeItem
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapDelete("/engagements/{engagementId:guid}/scope-items/{scopeItemId:guid}", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    }

    private static async Task<IResult> Handle(Guid scopeItemId, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var engagementId = access.EngagementId;
        var (transaction, status) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Share, ct);
        await using var _ = transaction;

        var item = await db.ScopeItems.SingleOrDefaultAsync(s => s.Id == scopeItemId && s.EngagementId == engagementId, ct);
        if (item is null)
        {
            return Problems.NotFound();
        }

        if (status == EngagementStatus.Closed)
        {
            return Problems.Conflict("engagement-closed");
        }

        if (await db.Assessments.AnyAsync(a => a.EngagementId == engagementId && a.ScopeItemId == scopeItemId, ct))
        {
            return Problems.Conflict("scope-item-in-use");
        }

        db.ScopeItems.Remove(item);
        db.AddAudit(access.Caller.Id, engagementId, "scope_item.deleted", "scope_item", item.Id,
            new { item.Type, item.Name });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
