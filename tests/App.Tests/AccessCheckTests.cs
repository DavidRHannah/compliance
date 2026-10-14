using App.Common.Access;
using App.Data.Entities;

namespace App.Tests;

/// <summary>The order of the access check, with no database or HTTP.</summary>
public class AccessCheckTests
{
    private static readonly Guid MyClient = Guid.NewGuid();
    private static readonly Guid OtherClient = Guid.NewGuid();

    private static readonly AllowedRoles AnyRole = new(UserRole.Admin, UserRole.Consultant, UserRole.Client);
    private static readonly AllowedRoles StaffOnly = new(UserRole.Admin, UserRole.Consultant);

    private static Caller Consultant(bool disabled = false) =>
        new(Guid.NewGuid(), UserRole.Consultant, null, disabled);

    private static Caller ClientUser() =>
        new(Guid.NewGuid(), UserRole.Client, MyClient, false);

    private static EngagementRef EngagementOf(Guid clientId) => new(Guid.NewGuid(), clientId);

    [Fact]
    public void ClientUserOfAnotherClient_OnStaffOnlyRoute_Gets404Not403()
    {
        var outcome = AccessCheck.Decide(ClientUser(), EngagementOf(OtherClient), true, StaffOnly);

        Assert.Equal(AccessOutcome.NotFound, outcome);
    }

    [Fact]
    public void ClientUserOfOwnClient_OnStaffOnlyRoute_Gets403()
    {
        var outcome = AccessCheck.Decide(ClientUser(), EngagementOf(MyClient), true, StaffOnly);

        Assert.Equal(AccessOutcome.Forbidden, outcome);
    }

    [Fact]
    public void MissingEngagement_Gets404_EvenForStaffOnlyRoute()
    {
        var outcome = AccessCheck.Decide(ClientUser(), null, true, StaffOnly);

        Assert.Equal(AccessOutcome.NotFound, outcome);
    }

    [Fact]
    public void DisabledUser_Gets403_BeforeEngagementIsLookedAt()
    {
        var outcome = AccessCheck.Decide(Consultant(disabled: true), null, true, AnyRole);

        Assert.Equal(AccessOutcome.Disabled, outcome);
    }

    [Fact]
    public void UnknownCaller_IsUnauthenticated()
    {
        var outcome = AccessCheck.Decide(null, EngagementOf(MyClient), true, AnyRole);

        Assert.Equal(AccessOutcome.Unauthenticated, outcome);
    }

    [Fact]
    public void Consultant_SeesEveryClientsEngagements()
    {
        var outcome = AccessCheck.Decide(Consultant(), EngagementOf(OtherClient), true, StaffOnly);

        Assert.Equal(AccessOutcome.Allowed, outcome);
    }

    [Fact]
    public void RouteWithoutEngagement_SkipsEngagementCheck()
    {
        var outcome = AccessCheck.Decide(ClientUser(), null, false, AnyRole);

        Assert.Equal(AccessOutcome.Allowed, outcome);
    }

    [Fact]
    public void RouteWithoutDeclaredRoles_IsRefused()
    {
        var outcome = AccessCheck.Decide(Consultant(), null, false, null);

        Assert.Equal(AccessOutcome.Forbidden, outcome);
    }
}
