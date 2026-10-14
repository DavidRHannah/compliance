namespace App.Tests;

public class ReviewTests
{
    [Fact(Skip = "Pending")]
    [Requirement("RV-1", "A consultant can view each current assessment with its verdict, rationale, citations, and the source location of each citation.")]
    public void Consultant_ViewsAssessmentWithCitationLocations()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-2", "A consultant records a decision on an assessment: accept the verdict, or override it with a different one. Accepting a preliminary pass makes the final verdict pass, accepting a preliminary fail makes it fail, and accepting insufficient evidence keeps it. Interpretation required cannot be accepted, only overridden.")]
    public void Decision_AcceptsOrOverrides()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-3", "An override must include a note.")]
    public void Override_RequiresNote()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-4", "A consultant's final verdict is one of: pass, fail or insufficient evidence. Not applicable comes only from applicability (EN-4).")]
    public void FinalVerdict_IsPassFailOrInsufficientEvidence()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-5", "A decision is never edited or deleted. A later decision on the same assessment becomes the decision of record.")]
    public void LatestDecision_IsDecisionOfRecord()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-6", "The backend records who performed each upload, scope change, applicability change, run and decision, and when.")]
    public void Changes_AreRecordedWithActorAndTime()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-7", "When a new run supersedes an assessment that has a decision, the new assessment is flagged for re-review and shows the earlier decision.")]
    public void SupersededDecidedAssessment_IsFlagged()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-8", "When a cited document is deleted or replaced, each assessment that cites it is flagged for re-review, and the flag names the document.")]
    public void DeletedCitedDocument_FlagsAssessments()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-9", "A flag does not change a verdict or start a run. The consultant decides what to do.")]
    public void Flag_DoesNotChangeVerdictOrStartRun()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-10", "A consultant can export an engagement's current verdicts, rationales, citations and decisions as an XLSX file.")]
    public void Export_ProducesXlsx()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-11", "A decision on an assessment that is no longer current is rejected, and the consultant is shown the current assessment.")]
    public void DecisionOnNonCurrentAssessment_IsRejected()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-12", "Recording a decision clears the assessment's open flags. A consultant can also clear a flag without recording a decision.")]
    public void Decision_ClearsOpenFlags()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("RV-13", "After a re-run, results show a control's latest consultant verdict, marked under review, until a consultant decides on the new assessment.")]
    public void Results_ShowLatestVerdictUnderReview()
    {
    }
}
