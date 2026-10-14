using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using App.Common.Auth;
using App.Integrations.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Integrations.Aws;

/// <summary>
/// Cognito user pool accounts. The pool signs in with email as the username attribute, so
/// Cognito's username for a user is their subject, and every admin call can use it.
/// Not covered by tests: there is no Cognito emulator. Check against a development pool.
/// </summary>
public sealed class CognitoUserDirectory(IAmazonCognitoIdentityProvider cognito, IOptions<AuthSettings> settings)
    : IUserDirectory
{
    private string PoolId => settings.Value.UserPoolId;

    public async Task<string> InviteAsync(string email, CancellationToken ct)
    {
        var response = await cognito.AdminCreateUserAsync(new AdminCreateUserRequest
        {
            UserPoolId = PoolId,
            Username = email,
            DesiredDeliveryMediums = ["EMAIL"],
            UserAttributes =
            [
                new AttributeType { Name = "email", Value = email },
                new AttributeType { Name = "email_verified", Value = "true" },
            ],
        }, ct);

        return response.User.Attributes?.FirstOrDefault(a => a.Name == "sub")?.Value
            ?? throw new InvalidOperationException("Cognito returned no subject for the new user.");
    }

    public async Task<bool> ResendInviteAsync(string subject, CancellationToken ct)
    {
        var user = await cognito.AdminGetUserAsync(new AdminGetUserRequest { UserPoolId = PoolId, Username = subject }, ct);

        // Until the first sign-in, the account waits for a new password.
        if (user.UserStatus != UserStatusType.FORCE_CHANGE_PASSWORD)
        {
            return false;
        }

        await cognito.AdminCreateUserAsync(new AdminCreateUserRequest
        {
            UserPoolId = PoolId,
            Username = subject,
            MessageAction = MessageActionType.RESEND,
            DesiredDeliveryMediums = ["EMAIL"],
        }, ct);
        return true;
    }

    public async Task DisableAsync(string subject, CancellationToken ct)
    {
        await cognito.AdminDisableUserAsync(new AdminDisableUserRequest { UserPoolId = PoolId, Username = subject }, ct);
        await cognito.AdminUserGlobalSignOutAsync(new AdminUserGlobalSignOutRequest { UserPoolId = PoolId, Username = subject }, ct);
    }

    public async Task EnableAsync(string subject, CancellationToken ct)
    {
        await cognito.AdminEnableUserAsync(new AdminEnableUserRequest { UserPoolId = PoolId, Username = subject }, ct);
    }

    public async Task DeleteAsync(string subject, CancellationToken ct)
    {
        await cognito.AdminDeleteUserAsync(new AdminDeleteUserRequest { UserPoolId = PoolId, Username = subject }, ct);
    }
}
