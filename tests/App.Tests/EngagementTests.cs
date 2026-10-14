namespace App.Tests;

public class EngagementTests
{
    [Fact(Skip = "Pending")]
    [Requirement("EN-1", "A consultant can create an engagement for a client.")]
    public void Consultant_CanCreateEngagement()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("EN-2", "An engagement is in one of three states: draft, active or closed. A closed engagement is read-only. An engagement moves from draft to active to closed, and never back. A run can start only while it is active.")]
    public void Engagement_MovesDraftActiveClosedOnly()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("EN-3", "Every ISO 27001 control is applicable to a new engagement by default.")]
    public void NewEngagement_HasEveryControlApplicable()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("EN-4", "A consultant can mark a control as not applicable, and must give a justification.")]
    public void NotApplicable_RequiresJustification()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("EN-5", "Consultants and client users can add and edit scope items of type system, asset or vendor.")]
    public void ScopeItems_CanBeAddedAndEdited()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("EN-6", "A scope item that an assessment refers to cannot be deleted.")]
    public void ScopeItemInUse_CannotBeDeleted()
    {
    }

    [Fact(Skip = "Withdrawn")]
    [Requirement("EN-7", "Withdrawn. The organization profile is removed.")]
    public void OrganizationProfile_Withdrawn()
    {
    }

    [Fact(Skip = "Withdrawn")]
    [Requirement("EN-8", "Withdrawn. Implementation statements are removed.")]
    public void ImplementationStatements_Withdrawn()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("EN-9", "The lead consultant must be an active consultant or admin. Leading an engagement gives no extra access.")]
    public void LeadConsultant_IsActiveConsultantWithNoExtraAccess()
    {
    }
}
