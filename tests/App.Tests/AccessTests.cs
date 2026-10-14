namespace App.Tests;

public class AccessTests
{
    [Fact(Skip = "Pending")]
    [Requirement("AC-1", "Every request, except the health check, must carry a valid token from the identity provider.")]
    public void RequestWithoutToken_IsRejected()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-2", "Every user must sign in with multi-factor authentication.")]
    public void SignIn_RequiresMfa()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-3", "Users are created by invitation. There is no self-registration.")]
    public void Users_AreCreatedOnlyByInvitation()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-4", "A user's role and client are held by the backend, not by the identity provider.")]
    public void RoleAndClient_ComeFromBackend()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-5", "A client user can access only the engagements of their own client.")]
    public void ClientUser_SeesOnlyOwnClientsEngagements()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-6", "No operation may read or use data from an engagement other than the one requested.")]
    public void Operations_NeverUseAnotherEngagementsData()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("AC-7", "An admin can disable a user. A disabled user cannot sign in, and the backend refuses their requests. An admin cannot disable themselves or the last admin. A disabled user can be enabled again.")]
    public void Admin_CanDisableAndEnableUsers()
    {
    }
}
