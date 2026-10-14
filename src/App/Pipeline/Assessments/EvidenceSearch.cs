using System.Text.RegularExpressions;
using App.Data;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace App.Pipeline.Assessments;

/// <summary>
/// Finds the chunks that may be evidence for a control. Each query runs a vector search and a
/// full-text search, 20 chunks each. The lists are merged by reciprocal rank fusion and the top 12 kept.
/// Every search filters by engagement and leaves out deleted and superseded chunks and chunks of
/// documents that are not ready (QR-1, DO-8, AS-2).
/// </summary>
public static partial class EvidenceSearch
{
    public const int PerSearch = 20;
    public const int Keep = 12;

    /// <summary>The chunk IDs, best first.</summary>
    public static async Task<List<Guid>> FindAsync(
        AppDbContext db, IEmbedder embedder, Guid engagementId, IReadOnlyList<string> queries, CancellationToken ct)
    {
        if (queries.Count == 0)
        {
            return [];
        }

        var vectors = await embedder.EmbedAsync(queries, ct);
        var model = embedder.ModelId;
        var lists = new List<IReadOnlyList<Guid>>();

        // SET LOCAL lasts for this transaction only. Iterative scan lets the index keep looking
        // when the filters remove rows, so a search still returns 20 chunks.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.iterative_scan = relaxed_order", ct);

        for (var i = 0; i < queries.Count; i++)
        {
            var vector = new Vector(vectors[i]);
            lists.Add(await VectorSearchAsync(db, engagementId, model, vector, ct));

            var terms = TsQuery(queries[i]);
            if (terms.Length > 0)
            {
                lists.Add(await TextSearchAsync(db, engagementId, terms, ct));
            }
        }

        await transaction.CommitAsync(ct);
        return Rrf.Merge(lists, Keep);
    }

    /// <summary>
    /// The WHERE repeats the partial HNSW index condition exactly (deleted_at and superseded_at),
    /// so the planner can use the index. The ready check is a scalar subquery so it stays a filter:
    /// a join would change the plan. {0} is the engagement, {1} the embedding model, {2} the query
    /// vector and {3} the number of rows. A test reads the plan of this text.
    /// </summary>
    public const string VectorSql = """
        SELECT c.id AS "Value"
        FROM document_chunks c
        WHERE c.engagement_id = {0}
          AND c.deleted_at IS NULL AND c.superseded_at IS NULL
          AND c.embedding_model = {1}
          AND (SELECT d.status FROM documents d WHERE d.id = c.document_id AND d.engagement_id = c.engagement_id) = 'ready'
        ORDER BY c.embedding <=> {2}
        LIMIT {3}
        """;

    public static Task<List<Guid>> VectorSearchAsync(
        AppDbContext db, Guid engagementId, string model, Vector vector, CancellationToken ct) =>
        db.Database.SqlQueryRaw<Guid>(VectorSql, engagementId, model, vector, PerSearch).ToListAsync(ct);
    public static Task<List<Guid>> TextSearchAsync(
        AppDbContext db, Guid engagementId, string tsQuery, CancellationToken ct) =>
        db.Database.SqlQuery<Guid>($"""
                SELECT c.id AS "Value"
                FROM document_chunks c, to_tsquery('english', {tsQuery}) q
                WHERE c.engagement_id = {engagementId}
                  AND c.deleted_at IS NULL AND c.superseded_at IS NULL
                  AND (SELECT d.status FROM documents d WHERE d.id = c.document_id AND d.engagement_id = c.engagement_id) = 'ready'
                  AND c.search_vector @@ q
                ORDER BY ts_rank_cd(c.search_vector, q) DESC, c.id
                LIMIT {PerSearch}
                """)
            .ToListAsync(ct);

    /// <summary>
    /// Any of the words matches, because a query is a phrase such as "information security policy"
    /// and a chunk rarely holds every word. Words hold only letters and digits, so the query text
    /// cannot break the tsquery syntax.
    /// </summary>
    public static string TsQuery(string text) =>
        string.Join(" | ", Word().Matches(text).Select(m => m.Value.ToLowerInvariant()).Distinct());

    [GeneratedRegex(@"[\p{L}\p{N}]{2,}")]
    private static partial Regex Word();
}
