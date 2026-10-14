namespace App.Tests;

public class AssessmentTests
{
    [Fact(Skip = "Pending")]
    [Requirement("AS-1", "Only a consultant can start an assessment run. A run never starts automatically.")]
    public void OnlyConsultant_CanStartRun()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-2", "A run assesses every applicable control, using only the ready documents of that engagement.")]
    public void Run_AssessesApplicableControlsWithReadyDocuments()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-3", "Each assessment has one verdict: preliminary pass, preliminary fail, interpretation required, or insufficient evidence.")]
    public void Assessment_HasOnePreliminaryVerdict()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-4", "Each assessment has a written rationale and zero or more citations.")]
    public void Assessment_HasRationaleAndCitations()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-5", "Each citation names one chunk and quotes text from it. The backend checks that the quoted text appears in that chunk.")]
    public void Citation_QuoteIsCheckedAgainstChunk()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-6", "A citation that fails the check is marked unverified and is not presented as evidence.")]
    public void UnverifiedCitation_IsNotPresented()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-7", "A preliminary pass or preliminary fail with no verified citation is recorded as insufficient evidence.")]
    public void PassOrFailWithoutVerifiedCitation_BecomesInsufficientEvidence()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-8", "Each run records the model and the prompt version it used. Each assessment records the assessment criteria version it used.")]
    public void Run_RecordsModelPromptAndCriteriaVersions()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-9", "An assessment is never edited or deleted. A new run adds new assessments and marks the earlier ones as superseded.")]
    public void Assessment_IsNeverEditedAndIsSupersededByNewRun()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-10", "A run that stops partway can resume without repeating the controls already assessed.")]
    public void StoppedRun_ResumesWithoutRepeatingControls()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-11", "Each assessment is made from three things: the control's text and its assessment criteria, the engagement's scope items, and the evidence found for that control.")]
    public void Assessment_UsesControlScopeAndEvidence()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-12", "Each piece of evidence is given to the model with its document's name, type and upload date.")]
    public void Evidence_IncludesDocumentNameTypeAndDate()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-13", "Text inside a document is treated as evidence only, never as an instruction to the system.")]
    public void DocumentText_IsNeverAnInstruction()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-14", "An engagement has at most one run queued or running at a time.")]
    public void Engagement_HasAtMostOneActiveRun()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AS-15", "A failed run can be resumed only if it is the engagement's latest run.")]
    public void OnlyLatestFailedRun_CanResume()
    {
    }
}
