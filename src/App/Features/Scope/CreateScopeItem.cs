using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;

namespace App.Features.Scope;

/// <summary>Consultants and client users add scope items (EN-5).</summary>
public static class CreateScopeItem
{
    public sealed record Request(ScopeItemType? Type, string? Name, List<string>? Aliases, string? Description);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/engagements/{engagementId:guid}/scope-items", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    }

    private static async Task<IResult> Handle(
        Request request, AccessContext access, AppDbContext db, IQueue queue, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (request.Type is null)
        {
            errors["type"] = ["Required: system, asset or vendor."];
        }

        if (string.IsNullOrEmpty(name))
        {
            errors["name"] = ["Required."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var engagementId = access.EngagementId;
        var (transaction, status) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Share, ct);
        await using var _ = transaction;
        if (status == EngagementStatus.Closed)
        {
            return Problems.Conflict("engagement-closed");
        }

        var item = new ScopeItem
        {
            EngagementId = engagementId,
            Type = request.Type!.Value,
            Name = name!,
            Aliases = ScopeItems.CleanAliases(request.Aliases),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedById = access.Caller.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ScopeItems.Add(item);
        db.AddAudit(access.Caller.Id, engagementId, "scope_item.created", "scope_item", item.Id,
            new { item.Type, item.Name, item.Aliases });

        var conflict = await ScopeItems.SaveAndRelinkAsync(db, transaction, queue, engagementId, ct);
        return conflict ?? Results.Created(
            $"/api/engagements/{engagementId}/scope-items/{item.Id}", ScopeItemResponse.From(item));
    }
}
