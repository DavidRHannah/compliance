namespace App.Tests;

public class ControlTests
{
    [Fact(Skip = "Pending")]
    [Requirement("CT-1", "Each control has assessment criteria. They state the evidence to expect, the conditions for a pass, the conditions for a fail, and whether evidence that the control operates is required.")]
    public void Criteria_HaveEvidencePassFailAndOperationFields()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("CT-2", "An admin can add a new version of a control's assessment criteria. An earlier version is never edited or deleted.")]
    public void CriteriaVersions_AreNeverEditedOrDeleted()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("CT-3", "Each control has one active version of its criteria. A run uses the versions that were active when it started.")]
    public void Run_UsesCriteriaActiveAtStart()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("CT-4", "A run cannot start while an applicable control has no active criteria.")]
    public void Run_CannotStartWithoutActiveCriteria()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("CT-5", "Assessment criteria belong to the firm and apply to every engagement.")]
    public void Criteria_ApplyToEveryEngagement()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("CT-6", "The control catalog is loaded by a developer task, not through the API. A control is never deleted.")]
    public void Catalog_IsLoadedByTaskAndNeverDeleted()
    {
    }
}
