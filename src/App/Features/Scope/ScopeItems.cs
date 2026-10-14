using System.Linq.Expressions;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace App.Features.Scope;

public sealed record ScopeItemResponse(
    Guid Id, ScopeItemType Type, string Name, IReadOnlyList<string> Aliases, string? Description,
    Guid CreatedById, DateTimeOffset CreatedAt)
{
    public static readonly Expression<Func<ScopeItem, ScopeItemResponse>> Projection =
        s => new ScopeItemResponse(s.Id, s.Type, s.Name, s.Aliases, s.Description, s.CreatedById, s.CreatedAt);

    public static ScopeItemResponse From(ScopeItem s) =>
        new(s.Id, s.Type, s.Name, s.Aliases, s.Description, s.CreatedById, s.CreatedAt);
}

/// <summary>What create and update share: field cleaning and the save that queues re-linking.</summary>
public static class ScopeItems
{
    /// <summary>Trimmed, without blanks or repeats (ignoring case).</summary>
    public static List<string> CleanAliases(IEnumerable<string>? aliases) =>
        (aliases ?? [])
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Saves, queues re-linking, then commits. The message goes first: re-linking can run again
    /// safely, so a message for a rolled-back change does no harm (DECISIONS.md #16).
    /// Returns a 409 for a duplicate name, or null on success.
    /// </summary>
    public static async Task<IResult?> SaveAndRelinkAsync(
        AppDbContext db, IDbContextTransaction transaction, IQueue queue, Guid engagementId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Problems.IsUniqueViolation(ex, "ix_scope_items_engagement_id_type_name"))
        {
            return Problems.Conflict("duplicate-scope-item", "The engagement already has a scope item of this type and name.");
        }

        await queue.SendAsync(Queues.Documents, new RelinkScopeMessage(engagementId), ct);
        await transaction.CommitAsync(ct);
        return null;
    }
}
