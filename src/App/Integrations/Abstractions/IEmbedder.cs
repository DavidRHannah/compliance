namespace App.Integrations.Abstractions;

/// <summary>Turns text into vectors. Search filters on <see cref="ModelId"/>, so vectors of different models never mix.</summary>
public interface IEmbedder
{
    string ModelId { get; }

    /// <summary>One 1024-dimension vector per text, in the same order.</summary>
    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}
