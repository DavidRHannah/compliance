using App.Common.Access;
using App.Common.Errors;
using App.Common.Paging;
using App.Data;
using App.Data.Entities;

namespace App.Features.EngagementResults;

/// <summary>
/// Results for every role, client users included. The items are paged by control ID like other lists,
/// but the totals always cover the whole engagement (DECISIONS.md #22).
/// </summary>
public static class GetResults
{
    public sealed record Response(IReadOnlyList<ResultItem> Items, string? NextCursor, ResultTotals Totals);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/results", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    }

    private static async Task<IResult> Handle(
        [AsParameters] PageQuery page, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var errors = page.Validate();
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var all = await ResultsQuery.LoadAsync(db, access.EngagementId, ct);
        var after = page.After;
        var rest = after is { } a ? all.Where(i => i.ControlId.CompareTo(a) > 0).ToList() : all;
        var items = rest.Take(page.Take).ToList();
        var next = rest.Count > page.Take ? PageQuery.Encode(items[^1].ControlId) : null;
        return Results.Ok(new Response(items, next, ResultsQuery.Totals(all)));
    }
}
