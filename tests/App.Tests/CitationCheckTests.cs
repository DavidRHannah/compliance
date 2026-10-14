using App.Data.Entities;
using App.Integrations.Abstractions;
using App.Pipeline.Assessments;

namespace App.Tests;

/// <summary>The pure rules of the assessment worker: citation checks, verdict rules and rank fusion.</summary>
public class CitationCheckTests
{
    private const string Content = "The information  security\npolicy is reviewed every year.";

    private static (Guid Id, IReadOnlyDictionary<Guid, string> Sent) Sent()
    {
        var id = Guid.NewGuid();
        return (id, new Dictionary<Guid, string> { [id] = Content });
    }

    [Fact]
    [Requirement("AS-5", "Each citation names one chunk and quotes text from it. The backend checks that the quoted text appears in that chunk.")]
    public void Quote_IsFound_IgnoringCaseAndSpacing()
    {
        var (id, sent) = Sent();

        var exact = CitationCheck.Check(new ModelCitation(id.ToString(), "information  security\npolicy"), sent);
        var loose = CitationCheck.Check(new ModelCitation(id.ToString(), "INFORMATION SECURITY POLICY is"), sent);

        // The offsets point into the original text, with its odd spacing.
        Assert.True(exact.IsVerified);
        Assert.Equal("information  security\npolicy", Content[exact.CharStart!.Value..exact.CharEnd!.Value]);
        Assert.Equal("information  security\npolicy is", Content[loose.CharStart!.Value..loose.CharEnd!.Value]);
    }

    [Theory]
    [InlineData("passwords are rotated")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("every year. And more")]
    public void Quote_NotInChunk_IsNotVerified(string quote)
    {
        var (id, sent) = Sent();

        var result = CitationCheck.Check(new ModelCitation(id.ToString(), quote), sent);

        Assert.False(result.IsVerified);
        Assert.Null(result.CharStart);
        Assert.Null(result.CharEnd);
    }

    [Fact]
    public void ChunkNotSent_IsNeverVerified()
    {
        var (_, sent) = Sent();
        var other = Guid.NewGuid();

        var notSent = CitationCheck.Check(new ModelCitation(other.ToString(), "information"), sent);
        var garbage = CitationCheck.Check(new ModelCitation("not-an-id", "information"), sent);

        Assert.False(notSent.IsVerified);
        Assert.Equal(other, notSent.ChunkId);
        Assert.False(garbage.IsVerified);
        Assert.Null(garbage.ChunkId);
    }

    [Theory]
    [InlineData(PreliminaryVerdict.PreliminaryPass, 0, PreliminaryVerdict.InsufficientEvidence)]
    [InlineData(PreliminaryVerdict.PreliminaryFail, 0, PreliminaryVerdict.InsufficientEvidence)]
    [InlineData(PreliminaryVerdict.PreliminaryPass, 1, PreliminaryVerdict.PreliminaryPass)]
    [InlineData(PreliminaryVerdict.PreliminaryFail, 2, PreliminaryVerdict.PreliminaryFail)]
    [InlineData(PreliminaryVerdict.InterpretationRequired, 0, PreliminaryVerdict.InterpretationRequired)]
    [InlineData(PreliminaryVerdict.InsufficientEvidence, 0, PreliminaryVerdict.InsufficientEvidence)]
    [Requirement("AS-7", "A preliminary pass or preliminary fail with no verified citation is recorded as insufficient evidence.")]
    public void VerdictRule_NeedsAVerifiedCitationForPassOrFail(PreliminaryVerdict verdict, int verified, PreliminaryVerdict expected)
    {
        Assert.Equal(expected, VerdictRules.Apply(verdict, verified));
    }

    [Fact]
    public void Rrf_RewardsItemsInSeveralLists_AndKeepsTheTop()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var d = Guid.CreateVersion7();

        // b is second in both lists, a and c are first in one each, d is last in one.
        var merged = Rrf.Merge([[a, b, d], [c, b]], top: 3);

        Assert.Equal(b, merged[0]);
        Assert.Equal(3, merged.Count);
        Assert.DoesNotContain(d, merged);
        Assert.Equal(merged, Rrf.Merge([[a, b, d], [c, b]], top: 3));
    }

    [Fact]
    public void Rrf_BreaksTiesBySmallerId()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() }.Order().ToArray();

        // Both score the same, so the smaller ID comes first whichever list it is in.
        Assert.Equal(ids, Rrf.Merge([[ids[1]], [ids[0]]], top: 12));
    }
}
