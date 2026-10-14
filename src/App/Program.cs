using App.Data;
using App.Tasks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    AppDbContext.Configure(options, builder.Configuration.GetConnectionString("Database")));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// A command runs a one-off task instead of the API.
if (args.FirstOrDefault() == "migrate")
{
    await Migrate.RunAsync(app.Services);
    return;
}

if (app.Environment.IsDevelopment())
{
    // The OpenAPI document at /openapi/v1.json and a page to browse it at /scalar.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");

app.Run();
