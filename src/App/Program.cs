using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.BedrockRuntime;
using Amazon.CognitoIdentityProvider;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using App.Common.Access;
using App.Common.Auth;
using App.Data;
using App.Features.Assessments;
using App.Features.Clients;
using App.Features.Decisions;
using App.Features.Engagements;
using App.Features.Export;
using App.Features.Me;
using App.Features.EngagementResults;
using App.Features.Runs;
using App.Features.Scope;
using App.Features.Users;
using App.Integrations;
using App.Integrations.Abstractions;
using App.Integrations.Aws;
using App.Pipeline;
using App.Pipeline.Assessments;
using App.Tasks;
using Npgsql;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// One JSON line per log entry for CloudWatch.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.AddJsonConsole();
}

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
// Enums go over the wire as snake_case strings, the same names the database uses.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));
// In AWS the task gets the host and the Aurora secret's username and password as separate settings.
// Rotating the secret needs no change to the app.
// Read when the context is first built, so the tests' settings are already in place.
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("Database");
    if (string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(config["Database:Host"]))
    {
        connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = config["Database:Host"],
            Port = config.GetValue("Database:Port", 5432),
            Database = config["Database:Name"],
            Username = config["Database:Username"],
            Password = config["Database:Password"],
            SslMode = SslMode.Require,
        }.ConnectionString;
    }

    AppDbContext.Configure(options, connectionString);
});
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddCognitoAuth(builder.Environment);
builder.Services.AddScoped<AccessContext>();

// AWS clients use the task role; no access keys (QR-3). Adaptive retries with backoff cover
// throttled model calls.
var awsOptions = builder.Configuration.GetAWSOptions();
awsOptions.DefaultClientConfig.RetryMode = RequestRetryMode.Adaptive;
awsOptions.DefaultClientConfig.MaxErrorRetry = 8;
builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonCognitoIdentityProvider>();
builder.Services.AddAWSService<IAmazonSQS>();
builder.Services.AddAWSService<IAmazonBedrockRuntime>();
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddScoped<IUserDirectory, CognitoUserDirectory>();
builder.Services.Configure<CatalogSettings>(builder.Configuration.GetSection("Catalog"));
builder.Services.AddScoped<ICatalogSource, S3CatalogSource>();

var bedrock = builder.Services.AddOptions<BedrockSettings>().BindConfiguration("Bedrock").ValidateDataAnnotations();
builder.Services.Configure<QueueSettings>(options => builder.Configuration.GetSection("Queues").Bind(options.Queues));
builder.Services.AddOptions<WorkerSettings>().BindConfiguration("Worker");

// Outside development, missing model settings stop the app at startup.
if (!builder.Environment.IsDevelopment())
{
    bedrock.ValidateOnStart();
}

builder.Services.AddSingleton<IEmbedder, TitanEmbedder>();
builder.Services.AddSingleton<IControlAssessor, BedrockControlAssessor>();

// SQS when the queue URLs are set. Otherwise only development has a stand-in that drops messages.
if (!string.IsNullOrEmpty(builder.Configuration["Queues:Assessments:Url"]))
{
    builder.Services.AddSingleton<SqsQueue>();
    builder.Services.AddSingleton<IQueue>(s => s.GetRequiredService<SqsQueue>());
    builder.Services.AddSingleton<IQueueReceiver>(s => s.GetRequiredService<SqsQueue>());
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IQueue, NullQueue>();
}

builder.Services.AddScoped<AssessControl>();
builder.Services.AddScoped<AssessmentWriter>();

// The same image runs as API or worker: the consumers and the sweeper start only when enabled.
if (builder.Configuration.GetValue<bool>("Worker:Enabled"))
{
    builder.Services.AddHostedService<AssessmentConsumer>();
    builder.Services.AddHostedService<Sweeper>();
}
var app = builder.Build();

// A command runs a one-off task instead of the API.
switch (args.FirstOrDefault())
{
    case "migrate":
        await Migrate.RunAsync(app.Services);
        return;
    case "import-catalog":
        await ImportCatalog.RunAsync(app.Services, args);
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
api.MapRuns();
api.MapAssessments();
api.MapDecisions();
api.MapResults();
api.MapExport();

app.Run();
