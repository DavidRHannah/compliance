using System.Collections.Concurrent;
using App.Data.Entities;
using App.Integrations.Abstractions;
using App.Integrations.Aws.Prompts;

namespace App.Tests.Infrastructure;

/// <summary>
/// A stand-in for the model. It records what it was given. By default it passes and cites the first
/// chunk with a quote copied from it; a test can change <see cref="Respond"/> or make it fail.
/// </summary>
public sealed class FakeControlAssessor : IControlAssessor
{
    public string ModelId => "fake-model";

    public string PromptVersion => PromptTemplate.CurrentVersion;

    public ConcurrentQueue<ControlContext> Contexts { get; } = new();

    public Func<ControlContext, AssessorResult> Respond { get; set; } = context =>
    {
        var first = context.Evidence[0];
        var quote = string.Join(' ', first.Content.Split(' ').Take(4));
        return new AssessorResult(PreliminaryVerdict.PreliminaryPass, "The evidence supports the control.",
            [new ModelCitation(first.ChunkId.ToString(), quote)]);
    };

    public Task<AssessorResult> AssessAsync(ControlContext context, CancellationToken ct)
    {
        Contexts.Enqueue(context);
        return Task.FromResult(Respond(context));
    }
}
