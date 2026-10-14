using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.CognitoIdentityProvider;
using App.Common.Access;
using App.Common.Auth;
using App.Data;
using App.Features.Clients;
using App.Features.Engagements;
using App.Features.Me;
using App.Features.Scope;
using App.Features.Users;
using App.Integrations;
using App.Integrations.Abstractions;
using App.Integrations.Aws;
using App.Tasks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
// Enums go over the wire as snake_case strings, the same names the database uses.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));
builder.Services.AddDbContext<AppDbContext>(options =>
    AppDbContext.Configure(options, builder.Configuration.GetConnectionString("Database")));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddCognitoAuth(builder.Environment);
builder.Services.AddScoped<AccessContext>();

// AWS clients use the task role; no access keys (QR-3).
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonCognitoIdentityProvider>();
builder.Services.AddScoped<IUserDirectory, CognitoUserDirectory>();

// The SQS queue comes with the pipeline. Until then only development has a stand-in.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IQueue, NullQueue>();
}

var app = builder.Build();

// A command runs a one-off task instead of the API.
if (args.FirstOrDefault() == "migrate")
{
    await Migrate.RunAsync(app.Services);
    return;
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // The OpenAPI document at /openapi/v1.json and a page to browse it at /scalar.
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapHealthChecks("/health").AllowAnonymous();

// Every route under /api passes the access check before its handler runs.
var api = app.MapGroup("/api").WithAccessCheck();
GetMe.Map(api);
api.MapClients();
api.MapUsers();
api.MapEngagements();
api.MapScope();

app.Run();
