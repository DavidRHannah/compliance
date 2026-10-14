using System.Text.RegularExpressions;
using App.Integrations.Abstractions;

namespace App.Tests.Infrastructure;

/// <summary>
/// A deterministic stand-in for Titan. Each word adds to one of 1024 slots and the vector is normalized,
/// so texts that share words get close vectors.
/// </summary>
public sealed partial class FakeEmbedder : IEmbedder
{
    public const string Model = "fake-embed-v1";

    public string ModelId => Model;

    public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
        Task.FromResult(texts.Select(Embed).ToArray());

    public static float[] Embed(string text)
    {
        var vector = new float[1024];
        foreach (Match word in Word().Matches(text.ToLowerInvariant()))
        {
            // FNV-1a, because string.GetHashCode differs between runs.
            var hash = 2166136261u;
            foreach (var c in word.Value)
            {
                hash = (hash ^ c) * 16777619u;
            }

            vector[hash % 1024] += 1;
        }

        var length = MathF.Sqrt(vector.Sum(v => v * v));
        if (length == 0)
        {
            vector[0] = 1;
            return vector;
        }

        return vector.Select(v => v / length).ToArray();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]{2,}")]
    private static partial Regex Word();
}
