using System.Net.Http.Headers;
using App.Data.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Infrastructure;

/// <summary>The API, connected to the test container.</summary>
public sealed class ApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = postgres.ConnectionString,
            ["Auth:Authority"] = TestTokens.Issuer,
            ["Auth:ClientId"] = TestTokens.ClientId,
        }));

        // Trust the test key and issuer directly, so no metadata is downloaded.
        // The app's own token checks (signature, issuer, expiry, client_id) still run.
        builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                options.ConfigurationManager = null;
                options.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
            }));
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
