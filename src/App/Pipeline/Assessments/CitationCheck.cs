using App.Integrations.Abstractions;

namespace App.Pipeline.Assessments;

/// <summary>A citation after the check. ChunkId is null when the model gave no valid ID. Offsets are set only when it is verified.</summary>
public sealed record CheckedCitation(Guid? ChunkId, string Quote, int? CharStart, int? CharEnd)
{
    public bool IsVerified => CharStart is not null;
}

/// <summary>
/// A citation is verified when its chunk was one of those sent and its quote appears in that
/// chunk, ignoring case and whitespace (AS-5). Offsets point into the original content.
/// </summary>
public static class CitationCheck
{
    public static CheckedCitation Check(ModelCitation citation, IReadOnlyDictionary<Guid, string> sentChunks)
    {
        if (!Guid.TryParse(citation.ChunkId, out var chunkId))
        {
            return new CheckedCitation(null, citation.Quote, null, null);
        }

        // A chunk that was not sent cannot be verified, whatever the quote says.
        if (!sentChunks.TryGetValue(chunkId, out var content))
        {
            return new CheckedCitation(chunkId, citation.Quote, null, null);
        }

        var found = Find(content, citation.Quote);
        return found is var (start, end)
            ? new CheckedCitation(chunkId, citation.Quote, start, end)
            : new CheckedCitation(chunkId, citation.Quote, null, null);
    }

    /// <summary>The range of the original text that matches the quote, or null. An empty quote never matches.</summary>
    public static (int Start, int End)? Find(string content, string quote)
    {
        var needle = new string(quote.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());
        if (needle.Length == 0)
        {
            return null;
        }

        // The content without whitespace, with each character's position in the original.
        var stripped = new List<char>(content.Length);
        var positions = new List<int>(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            if (!char.IsWhiteSpace(content[i]))
            {
                stripped.Add(char.ToLowerInvariant(content[i]));
                positions.Add(i);
            }
        }

        var index = new string(stripped.ToArray()).IndexOf(needle, StringComparison.Ordinal);
        return index < 0 ? null : (positions[index], positions[index + needle.Length - 1] + 1);
    }
}
