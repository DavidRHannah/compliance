using System.Text.Json;
using System.Text.Json.Serialization;
using App.Common.Access;
using App.Common.Auth;
using App.Data;
using App.Features.Engagements;
using App.Features.Me;
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
GetEngagement.Map(api);

app.Run();
