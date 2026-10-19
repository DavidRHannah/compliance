using Amazon.CDK;
using Infra;

var app = new App();

string Context(string key) => app.Node.TryGetContext(key)?.ToString() ?? "";

// No default for the model: the ID depends on what the account has enabled in Bedrock.
var model = Context("assessmentModelId");
if (string.IsNullOrEmpty(model))
{
    throw new InvalidOperationException("Pass the Bedrock model: -c assessmentModelId=<model or inference profile ID>.");
}

var settings = new InfraSettings(
    EnvName: Context("env") is { Length: > 0 } env ? env : "dev",
    ImageTag: Context("imageTag") is { Length: > 0 } tag ? tag : "latest",
    AssessmentModelId: model,
    FrontendOrigin: Context("frontendOrigin") is { Length: > 0 } origin ? origin : "http://localhost:5173",
    AlarmEmail: Context("alarmEmail") is { Length: > 0 } email ? email : null,
    NatGateway: Context("natGateway") == "true");

var environment = new Amazon.CDK.Environment
{
    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
    Region = "us-east-1",
};

var platform = new PlatformStack(app, "Platform", settings,
    new StackProps { Env = environment, StackName = $"compliance-{settings.EnvName}-platform" });
new ServicesStack(app, "Services", settings, platform,
    new StackProps { Env = environment, StackName = $"compliance-{settings.EnvName}-services" });

app.Synth();
