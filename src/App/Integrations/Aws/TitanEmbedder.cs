using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using App.Integrations.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Integrations.Aws;

/// <summary>Titan Text Embeddings V2 through Bedrock: 1024 dimensions, normalized.</summary>
public sealed class TitanEmbedder(IAmazonBedrockRuntime bedrock, IOptions<BedrockSettings> settings) : IEmbedder
{
    public string ModelId => settings.Value.EmbeddingModelId;

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var vectors = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(new { inputText = texts[i], dimensions = 1024, normalize = true });
            var response = await bedrock.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId = ModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(body),
            }, ct);

            using var json = await JsonDocument.ParseAsync(response.Body, cancellationToken: ct);
            vectors[i] = json.RootElement.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        }

        return vectors;
    }
}
