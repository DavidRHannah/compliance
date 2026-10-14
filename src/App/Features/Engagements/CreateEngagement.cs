using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Engagements;

/// <summary>
/// Creates a draft engagement with ISO 27001:2022 and one applicable row per control (EN-1, EN-3).
/// </summary>
public static class CreateEngagement
{
    public const string FrameworkKey = "iso_27001";
    public const string FrameworkVersion = "2022";

    public sealed record Request(Guid? ClientId, string? Name, Guid? LeadConsultantId);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/engagements", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(Request request, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (request.ClientId is null)
        {
            errors["clientId"] = ["Required."];
        }

        if (string.IsNullOrEmpty(name))
        {
            errors["name"] = ["Required."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var clientId = request.ClientId!.Value;
        if (!await db.Clients.AnyAsync(c => c.Id == clientId, ct))
        {
            return Problems.BadRequest("invalid-reference", "The client does not exist.");
        }

        var leadId = request.LeadConsultantId ?? access.Caller.Id;
        if (!await LeadRules.IsValidLeadAsync(db, leadId, ct))
        {
            return Problems.BadRequest("invalid-lead", "The lead must be an enabled consultant or admin.");
        }

        var frameworkId = await db.Frameworks
            .Where(f => f.Key == FrameworkKey && f.Version == FrameworkVersion)
            .Select(f => (Guid?)f.Id)
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The control catalog is not loaded. Run the import-catalog task.");

        var controlIds = await db.Controls
            .Where(c => c.FrameworkId == frameworkId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var engagement = new Engagement
        {
            ClientId = clientId,
            LeadConsultantId = leadId,
            Name = name!,
            Status = EngagementStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Engagements.Add(engagement);
        db.EngagementFrameworks.Add(new EngagementFramework { EngagementId = engagement.Id, FrameworkId = frameworkId });

        // Every control starts applicable; the database default sets is_applicable.
        db.EngagementControls.AddRange(controlIds.Select(controlId => new EngagementControl
        {
            EngagementId = engagement.Id,
            ControlId = controlId,
            FrameworkId = frameworkId,
        }));

        db.AddAudit(access.Caller.Id, engagement.Id, "engagement.created", "engagement", engagement.Id,
            new { clientId, name, leadConsultantId = leadId });

        // One SaveChanges is one transaction: the engagement never exists without its controls.
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/engagements/{engagement.Id}", EngagementResponse.From(engagement));
    }
}
