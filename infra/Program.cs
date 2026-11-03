using Amazon.CDK;
using Infra;

// <summary>
// Entry point for the CDK application.
// </summary>
var app = new App();

// The context values come from the CDK command line: cdk deploy -c key=value
string Context(string key) => app.Node.TryGetContext(key)?.ToString() ?? "";

// No default for the model: the ID depends on what the account has enabled in Bedrock.
var model = Context("assessmentModelId");
if (string.IsNullOrEmpty(model))
{
    throw new InvalidOperationException("Pass the Bedrock model: -c assessmentModelId=<model or inference profile ID>.");
}

// The settings are passed to the stacks and also used by the app to configure its services.
var settings = new InfraSettings(
    EnvName: Context("env") is { Length: > 0 } env ? env : "dev", // default to dev for local testing
    ImageTag: Context("imageTag") is { Length: > 0 } tag ? tag : "latest", // default to latest for local testing
    AssessmentModelId: model, // no default, must be passed in
    FrontendOrigin: Context("frontendOrigin") is { Length: > 0 } origin ? origin : "http://localhost:5173", // default to local dev server for local testing
    AlarmEmail: Context("alarmEmail") is { Length: > 0 } email ? email : null, // no default, optional
    NatGateway: Context("natGateway") == "true"); // default to false for local testing, optional

// The stacks are deployed to the same account and region as the CDK CLI is configured for.
var environment = new Amazon.CDK.Environment
{
    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
    Region = "us-east-1",
};

// save platform stack for the services stack to reference its resources.
var platformStackProps = new StackProps
{
    Env = environment,
    StackName = $"compliance-{settings.EnvName}-platform"
};
var platform = new PlatformStack(app, "Platform", settings, platformStackProps);

// The services stack needs the platform stack to be created first, so it can reference its resources.
var servicesStackProps = new StackProps
{
    Env = environment,
    StackName = $"compliance-{settings.EnvName}-services"
};
new ServicesStack(app, "Services", settings, platform, servicesStackProps);

// The CDK synthesizes the stacks into a CloudFormation template, which is then deployed by the CDK CLI.
app.Synth();
