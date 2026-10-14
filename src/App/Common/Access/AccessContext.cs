namespace App.Common.Access;

/// <summary>
/// The result of the access check, for the handler. Only the access filter sets it.
/// The getters throw if the check has not run, so a handler cannot use an unchecked ID (AC-6).
/// </summary>
public sealed class AccessContext
{
    private Caller? _caller;
    private Guid? _engagementId;

    public Caller Caller => _caller
        ?? throw new InvalidOperationException("The access check has not run.");

    public Guid EngagementId => _engagementId
        ?? throw new InvalidOperationException("This route has no checked engagement.");

    internal void Set(Caller caller, Guid? engagementId)
    {
        _caller = caller;
        _engagementId = engagementId;
    }
}
