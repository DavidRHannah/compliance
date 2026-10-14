using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Engagements;

/// <summary>Marks a control applicable or not. Not applicable needs a justification (EN-4).</summary>
public static class SetApplicability
{
    public sealed record Request(bool? IsApplicable, string? Justification);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPut("/engagements/{engagementId:guid}/controls/{controlId:guid}", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(
        Guid controlId, Request request, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        if (request.IsApplicable is null)
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["isApplicable"] = ["Required."] });
        }

        var isApplicable = request.IsApplicable.Value;
        var justification = isApplicable ? null : request.Justification?.Trim();
        if (!isApplicable && string.IsNullOrEmpty(justification))
        {
            return Problems.BadRequest("justification-required", "A control marked not applicable needs a justification.");
        }

        var engagementId = access.EngagementId;
        var (transaction, status) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Share, ct);
        await using var _ = transaction;

        var row = await db.EngagementControls
            .SingleOrDefaultAsync(ec => ec.EngagementId == engagementId && ec.ControlId == controlId, ct);
        if (row is null)
        {
            return Problems.NotFound();
        }

        if (status == EngagementStatus.Closed)
        {
            return Problems.Conflict("engagement-closed");
        }

        db.AddAudit(access.Caller.Id, engagementId, "applicability.set", "engagement_control", controlId, new
        {
            isApplicable = new { from = row.IsApplicable, to = isApplicable },
            justification = new { from = row.Justification, to = justification },
        });
        row.IsApplicable = isApplicable;
        row.Justification = justification;
        row.UpdatedById = access.Caller.Id;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var control = await db.Controls.AsNoTracking().SingleAsync(c => c.Id == controlId, ct);
        return Results.Ok(new ListEngagementControls.Response(
            controlId, control.RefCode, control.Theme, control.Title, row.IsApplicable, row.Justification, row.UpdatedAt));
    }
}
