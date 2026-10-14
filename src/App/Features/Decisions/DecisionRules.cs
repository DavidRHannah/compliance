using App.Data.Entities;

namespace App.Features.Decisions;

/// <summary>
/// Whether a final verdict keeps the system's verdict. "Accepted" is never stored: it is worked out
/// here (RV-2). Anything else is an override and needs a note (RV-3).
/// </summary>
public static class DecisionRules
{
    public static bool IsAccept(PreliminaryVerdict preliminary, FinalVerdict final) => (preliminary, final) switch
    {
        (PreliminaryVerdict.PreliminaryPass, FinalVerdict.Pass) => true,
        (PreliminaryVerdict.PreliminaryFail, FinalVerdict.Fail) => true,
        (PreliminaryVerdict.InsufficientEvidence, FinalVerdict.InsufficientEvidence) => true,
        _ => false,
    };
}
