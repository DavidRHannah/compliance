using System.Collections.Concurrent;
using App.Integrations.Abstractions;

namespace App.Tests.Infrastructure;

/// <summary>Stands in for Cognito: records each call, and remembers which invites were accepted.</summary>
public sealed class FakeUserDirectory : IUserDirectory
{
    public ConcurrentQueue<(string Call, string Value)> Calls { get; } = new();

    /// <summary>Subjects whose invitation was accepted, so a resend is refused.</summary>
    public ConcurrentDictionary<string, bool> Accepted { get; } = new();

    public Task<string> InviteAsync(string email, CancellationToken ct)
    {
        Calls.Enqueue(("invite", email));
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task<bool> ResendInviteAsync(string subject, CancellationToken ct)
    {
        Calls.Enqueue(("resend", subject));
        return Task.FromResult(!Accepted.ContainsKey(subject));
    }

    public Task DisableAsync(string subject, CancellationToken ct) => Record("disable", subject);

    public Task EnableAsync(string subject, CancellationToken ct) => Record("enable", subject);

    public Task DeleteAsync(string subject, CancellationToken ct) => Record("delete", subject);

    private Task Record(string call, string subject)
    {
        Calls.Enqueue((call, subject));
        return Task.CompletedTask;
    }
}
