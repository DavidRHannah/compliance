namespace App.Integrations.Abstractions;

/// <summary>
/// The identity provider's user accounts. It proves who a user is; role and client stay in
/// the users table (AC-4). Users are referred to by their subject, users.external_id.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Creates the account and sends the invitation. Returns the new subject.</summary>
    Task<string> InviteAsync(string email, CancellationToken ct);

    /// <summary>Sends the invitation again. Returns false if it was already accepted.</summary>
    Task<bool> ResendInviteAsync(string subject, CancellationToken ct);

    /// <summary>Blocks sign-in and signs the user out everywhere.</summary>
    Task DisableAsync(string subject, CancellationToken ct);

    Task EnableAsync(string subject, CancellationToken ct);

    /// <summary>Removes an account whose users row could not be saved.</summary>
    Task DeleteAsync(string subject, CancellationToken ct);
}
