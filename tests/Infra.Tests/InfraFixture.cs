using Amazon.CDK;
using Amazon.CDK.Assertions;
using Infra;

namespace Infra.Tests;

/// <summary>Synthesizes both stacks once. Needs Node.js, which the CDK runtime uses.</summary>
public sealed class InfraFixture
{
    public Template Platform { get; }
    public Template Services { get; }

    public InfraFixture()
    {
        var app = new Amazon.CDK.App();
        var settings = new InfraSettings(
            EnvName: "test",
            ImageTag: "test",
            AssessmentModelId: "us.anthropic.test-model-v1:0",
            FrontendOrigin: "https://frontend.example",
            AlarmEmail: "alerts@example.com",
            NatGateway: false);
        var env = new Amazon.CDK.Environment { Account = "111111111111", Region = "us-east-1" };

        var platform = new PlatformStack(app, "Platform", settings, new StackProps { Env = env });
        var services = new ServicesStack(app, "Services", settings, platform, new StackProps { Env = env });
        Platform = Template.FromStack(platform);
        Services = Template.FromStack(services);
    }
}

[CollectionDefinition(Name)]
public class InfraCollection : ICollectionFixture<InfraFixture>
{
    public const string Name = "Infra";
}
