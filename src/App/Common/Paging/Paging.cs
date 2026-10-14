using System.Linq.Expressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace App.Common.Paging;

/// <summary>One page of a list, and the cursor for the next page, or null on the last page.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// Keyset paging on UUIDv7 IDs, which sort by creation time. The cursor is the last ID of the
/// page, encoded so clients treat it as opaque (DECISIONS.md #15).
/// </summary>
public sealed record PageQuery(int? Limit, string? Cursor)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public int Take => Limit ?? DefaultLimit;

    /// <summary>Field errors for limit and cursor, or none.</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (Limit is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"Must be between 1 and {MaxLimit}."];
        }

        if (Cursor is not null && Decode(Cursor) is null)
        {
            errors["cursor"] = ["Not a cursor from this API."];
        }

        return errors;
    }

    /// <summary>The ID after which this page starts, or null for the first page.</summary>
    public Guid? After => Cursor is null ? null : Decode(Cursor);

    public static string Encode(Guid id) => WebEncoders.Base64UrlEncode(id.ToByteArray());

    private static Guid? Decode(string cursor)
    {
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor);
            return bytes.Length == 16 ? new Guid(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

public static class PagingExtensions
{
    /// <summary>
    /// Reads one page of an already filtered query: orders by the key, starts after the cursor,
    /// and reads one extra row to tell whether another page follows. The projection runs last,
    /// because EF cannot sort on a property of a record built in a projection.
    /// </summary>
    /// <param name="key">The row's ID, as EF sees it.</param>
    /// <param name="select">The response projection.</param>
    /// <param name="cursorOf">The same ID, read from a response item.</param>
    public static async Task<Page<TResult>> ToPageAsync<TRow, TResult>(
        this IQueryable<TRow> query,
        Expression<Func<TRow, Guid>> key,
        Expression<Func<TRow, TResult>> select,
        Func<TResult, Guid> cursorOf,
        PageQuery page,
        CancellationToken ct)
    {
        if (page.After is { } after)
        {
            // key.CompareTo(after) > 0, which EF translates to key > after in SQL.
            var compare = Expression.Call(key.Body, nameof(Guid.CompareTo), null, Expression.Constant(after));
            var isAfter = Expression.GreaterThan(compare, Expression.Constant(0));
            query = query.Where(Expression.Lambda<Func<TRow, bool>>(isAfter, key.Parameters));
        }

        var rows = await query.OrderBy(key).Take(page.Take + 1).Select(select).ToListAsync(ct);
        var hasMore = rows.Count > page.Take;
        var items = hasMore ? rows[..page.Take] : rows;
        var next = hasMore ? PageQuery.Encode(cursorOf(items[^1])) : null;
        return new Page<TResult>(items, next);
    }
}
