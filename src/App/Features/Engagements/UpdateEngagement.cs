using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Engagements;

public static class UpdateEngagement
{
    public sealed record Request(string? Name, Guid? LeadConsultantId);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPatch("/engagements/{engagementId:guid}", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(Request request, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (request.Name is not null && string.IsNullOrEmpty(name))
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["name"] = ["Must not be empty."] });
        }

        if (request.LeadConsultantId is { } leadId && !await LeadRules.IsValidLeadAsync(db, leadId, ct))
        {
            return Problems.BadRequest("invalid-lead", "The lead must be an enabled consultant or admin.");
        }

        var engagementId = access.EngagementId;
        var (transaction, status) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Share, ct);
        await using var _ = transaction;
        if (status == EngagementStatus.Closed)
        {
            return Problems.Conflict("engagement-closed");
        }

        var engagement = await db.Engagements.SingleAsync(e => e.Id == engagementId, ct);
        var changes = new Dictionary<string, object?>();
        if (name is not null && name != engagement.Name)
        {
            changes["name"] = new { from = engagement.Name, to = name };
            engagement.Name = name;
        }

        if (request.LeadConsultantId is { } newLead && newLead != engagement.LeadConsultantId)
        {
            changes["leadConsultantId"] = new { from = engagement.LeadConsultantId, to = newLead };
            engagement.LeadConsultantId = newLead;
        }

        if (changes.Count > 0)
        {
            db.AddAudit(access.Caller.Id, engagementId, "engagement.updated", "engagement", engagementId, changes);
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return Results.Ok(EngagementResponse.From(engagement));
    }
}
