using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Scope;

/// <summary>Changes only the fields sent (EN-5).</summary>
public static class UpdateScopeItem
{
    public sealed record Request(ScopeItemType? Type, string? Name, List<string>? Aliases, string? Description);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPatch("/engagements/{engagementId:guid}/scope-items/{scopeItemId:guid}", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    }

    private static async Task<IResult> Handle(
        Guid scopeItemId, Request request, AccessContext access, AppDbContext db, IQueue queue, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (request.Name is not null && string.IsNullOrEmpty(name))
        {
            return Problems.Validation(new Dictionary<string, string[]> { ["name"] = ["Must not be empty."] });
        }

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

        var changes = new Dictionary<string, object?>();
        if (request.Type is { } type && type != item.Type)
        {
            changes["type"] = new { from = item.Type, to = type };
            item.Type = type;
        }

        if (name is not null && name != item.Name)
        {
            changes["name"] = new { from = item.Name, to = name };
            item.Name = name;
        }

        if (request.Aliases is not null)
        {
            var aliases = ScopeItems.CleanAliases(request.Aliases);
            if (!aliases.SequenceEqual(item.Aliases))
            {
                changes["aliases"] = new { from = item.Aliases, to = aliases };
                item.Aliases = aliases;
            }
        }

        if (request.Description is not null)
        {
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            if (description != item.Description)
            {
                changes["description"] = new { from = item.Description, to = description };
                item.Description = description;
            }
        }

        if (changes.Count == 0)
        {
            return Results.Ok(ScopeItemResponse.From(item));
        }

        db.AddAudit(access.Caller.Id, engagementId, "scope_item.updated", "scope_item", item.Id, changes);
        var conflict = await ScopeItems.SaveAndRelinkAsync(db, transaction, queue, engagementId, ct);
        return conflict ?? Results.Ok(ScopeItemResponse.From(item));
    }
}
