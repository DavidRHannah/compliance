namespace Infra;

/// <summary>The values that differ between deploys. Read from CDK context in Program.cs.</summary>
public sealed record InfraSettings(
    string EnvName,
    string ImageTag,
    string AssessmentModelId,
    string FrontendOrigin,
    string? AlarmEmail,
    bool NatGateway)
{
    public const string EmbeddingModelId = "amazon.titan-embed-text-v2:0";

    /// <summary>Capacity ceiling of the Aurora cluster. The "at maximum" alarm watches it.</summary>
    public const double MaxAcu = 4;
}
