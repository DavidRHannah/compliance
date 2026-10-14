using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace App.Common.Auth;

/// <summary>The Cognito user pool and app client, from the "Auth" section.</summary>
public sealed class AuthSettings
{
    /// <summary>https://cognito-idp.{region}.amazonaws.com/{poolId}</summary>
    [Required]
    public string Authority { get; set; } = "";

    [Required]
    public string ClientId { get; set; } = "";

    /// <summary>For the admin calls that invite, disable and enable users.</summary>
    [Required]
    public string UserPoolId { get; set; } = "";
}

public static class AuthSetup
{
    /// <summary>
    /// Validates Cognito access tokens (AC-1). Roles are not checked here: the access filter
    /// does that after it loads the engagement, so a missing engagement gives 404, not 403.
    /// </summary>
    public static IServiceCollection AddCognitoAuth(this IServiceCollection services, IHostEnvironment environment)
    {
        var settings = services.AddOptions<AuthSettings>().BindConfiguration("Auth").ValidateDataAnnotations();

        // Outside development, missing settings stop the app at startup, not at the first request.
        if (!environment.IsDevelopment())
        {
            settings.ValidateOnStart();
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured when the options are first built, so settings added late (as the tests do) are seen.
        // Reads the raw settings, not the validated ones: the middleware builds these options on every
        // request, so in development, empty settings would otherwise break /health and Scalar too.
        // Without settings, a request with a token gets 401; startup validation covers other environments.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, config) =>
            {
                var auth = config.GetSection("Auth").Get<AuthSettings>() ?? new AuthSettings();
                var clientId = auth.ClientId;

                options.Authority = string.IsNullOrEmpty(auth.Authority) ? null : auth.Authority;
                options.MapInboundClaims = false;

                // Cognito access tokens have no "aud". They carry "client_id" instead.
                options.TokenValidationParameters.ValidateAudience = false;

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var claims = context.Principal;
                        var isAccessToken = claims?.FindFirst("token_use")?.Value == "access";
                        var isOurClient = claims?.FindFirst("client_id")?.Value == clientId;
                        if (!isAccessToken || !isOurClient)
                        {
                            context.Fail("Not an access token for this app client.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        // Every endpoint needs a valid token unless it says AllowAnonymous.
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
