using App.Features.Users;

namespace App.Tests;

/// <summary>The disable rule, with no database. The integration side is in AccessTests (AC-7).</summary>
public class UserTests
{
    private static readonly Guid Caller = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();

    [Fact]
    public void DisableCheck_RefusesSelf()
    {
        Assert.Equal("cannot-disable-self", DisableUser.Check(Caller, Caller, targetIsEnabledAdmin: true, enabledAdmins: 5));
    }

    [Fact]
    public void DisableCheck_RefusesLastAdmin()
    {
        Assert.Equal("last-admin", DisableUser.Check(Caller, Target, targetIsEnabledAdmin: true, enabledAdmins: 1));
    }

    [Fact]
    public void DisableCheck_AllowsAnotherAdmin_WhileOneRemains()
    {
        Assert.Null(DisableUser.Check(Caller, Target, targetIsEnabledAdmin: true, enabledAdmins: 2));
    }

    [Fact]
    public void DisableCheck_AllowsNonAdmin()
    {
        Assert.Null(DisableUser.Check(Caller, Target, targetIsEnabledAdmin: false, enabledAdmins: 1));
    }
}
