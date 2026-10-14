using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace App.Tests.Infrastructure;

/// <summary>Signs tokens shaped like Cognito access tokens, with a key only the tests hold.</summary>
public static class TestTokens
{
    public const string Issuer = "https://cognito-idp.test.amazonaws.com/test-pool";
    public const string ClientId = "test-app-client";

    public static readonly SecurityKey SigningKey = NewKey();

    public static SecurityKey NewKey() => new RsaSecurityKey(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString() };

    public static string Create(
        string subject,
        string tokenUse = "access",
        string clientId = ClientId,
        string issuer = Issuer,
        SecurityKey? key = null,
        bool expired = false,
        params Claim[] extraClaims)
    {
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("token_use", tokenUse),
            new("client_id", clientId),
        };
        claims.AddRange(extraClaims);

        // Expired by more than the validator's 5-minute clock skew.
        var issuedAt = expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: issuer,
            claims: claims,
            notBefore: issuedAt,
            expires: issuedAt.AddMinutes(5),
            signingCredentials: new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
