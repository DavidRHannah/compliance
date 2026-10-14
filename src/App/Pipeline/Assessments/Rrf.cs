namespace App.Pipeline.Assessments;

/// <summary>Reciprocal rank fusion: merges ranked lists into one.</summary>
public static class Rrf
{
    public const int K = 60;

    /// <summary>
    /// Each item scores the sum of 1 / (K + rank) over the lists it appears in, rank starting at 1.
    /// Ties go to the smaller ID, so the result is the same every time.
    /// </summary>
    public static List<Guid> Merge(IEnumerable<IReadOnlyList<Guid>> lists, int top)
    {
        var scores = new Dictionary<Guid, double>();
        foreach (var list in lists)
        {
            for (var i = 0; i < list.Count; i++)
            {
                scores[list[i]] = scores.GetValueOrDefault(list[i]) + 1.0 / (K + i + 1);
            }
        }

        return scores
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Key)
            .Take(top)
            .Select(s => s.Key)
            .ToList();
    }
}
