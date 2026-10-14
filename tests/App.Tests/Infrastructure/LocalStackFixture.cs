using Amazon.Runtime;
using Amazon.SQS;
using Testcontainers.LocalStack;

namespace App.Tests.Infrastructure;

/// <summary>One LocalStack container for a test class, with an SQS client pointed at it.</summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    private readonly LocalStackContainer container = new LocalStackBuilder("localstack/localstack:3.8").Build();

    public IAmazonSQS Sqs { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        Sqs = new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig { ServiceURL = container.GetConnectionString(), AuthenticationRegion = "us-east-1" });
    }

    public async Task DisposeAsync()
    {
        Sqs?.Dispose();
        await container.DisposeAsync();
    }
}
