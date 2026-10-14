using System.Net.Http.Headers;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Infrastructure;

/// <summary>The API, connected to the test container, with fakes for Cognito and the queue.</summary>
public sealed class ApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    public FakeUserDirectory Directory { get; } = new();
    public FakeQueue Queue { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = postgres.ConnectionString,
            ["Auth:Authority"] = TestTokens.Issuer,
            ["Auth:ClientId"] = TestTokens.ClientId,
            ["Auth:UserPoolId"] = "test-pool",
        }));

        builder.ConfigureTestServices(services =>
        {
            // Trust the test key and issuer directly, so no metadata is downloaded.
            // The app's own token checks (signature, issuer, expiry, client_id) still run.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.ConfigurationManager = null;
                options.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
            });

            services.AddSingleton<IUserDirectory>(Directory);
            services.AddSingleton<IQueue>(Queue);
        });
    }

    /// <summary>A client that sends a valid access token for the user.</summary>
    public HttpClient CreateClientFor(User user) => CreateClientWithToken(TestTokens.Create(user.ExternalId));

    public HttpClient CreateClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
