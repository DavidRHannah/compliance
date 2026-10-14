using App.Data.Entities;
using App.Features.Decisions;

namespace App.Tests;

public class DecisionRulesTests
{
    [Theory]
    [InlineData(PreliminaryVerdict.PreliminaryPass, FinalVerdict.Pass, true)]
    [InlineData(PreliminaryVerdict.PreliminaryPass, FinalVerdict.Fail, false)]
    [InlineData(PreliminaryVerdict.PreliminaryPass, FinalVerdict.InsufficientEvidence, false)]
    [InlineData(PreliminaryVerdict.PreliminaryFail, FinalVerdict.Fail, true)]
    [InlineData(PreliminaryVerdict.PreliminaryFail, FinalVerdict.Pass, false)]
    [InlineData(PreliminaryVerdict.PreliminaryFail, FinalVerdict.InsufficientEvidence, false)]
    [InlineData(PreliminaryVerdict.InsufficientEvidence, FinalVerdict.InsufficientEvidence, true)]
    [InlineData(PreliminaryVerdict.InsufficientEvidence, FinalVerdict.Pass, false)]
    [InlineData(PreliminaryVerdict.InsufficientEvidence, FinalVerdict.Fail, false)]
    [InlineData(PreliminaryVerdict.InterpretationRequired, FinalVerdict.Pass, false)]
    [InlineData(PreliminaryVerdict.InterpretationRequired, FinalVerdict.Fail, false)]
    [InlineData(PreliminaryVerdict.InterpretationRequired, FinalVerdict.InsufficientEvidence, false)]
    public void IsAccept_OnlyWhenFinalKeepsPreliminary(PreliminaryVerdict preliminary, FinalVerdict final, bool expected)
    {
        Assert.Equal(expected, DecisionRules.IsAccept(preliminary, final));
    }
}
