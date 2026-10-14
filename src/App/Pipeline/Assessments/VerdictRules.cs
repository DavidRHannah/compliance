using App.Data.Entities;

namespace App.Pipeline.Assessments;

public static class VerdictRules
{
    /// <summary>
    /// A preliminary pass or fail with no verified citation becomes insufficient evidence (AS-7).
    /// Interpretation required and insufficient evidence need no citation.
    /// </summary>
    public static PreliminaryVerdict Apply(PreliminaryVerdict verdict, int verifiedCitations) =>
        verdict is PreliminaryVerdict.PreliminaryPass or PreliminaryVerdict.PreliminaryFail && verifiedCitations == 0
            ? PreliminaryVerdict.InsufficientEvidence
            : verdict;
}
