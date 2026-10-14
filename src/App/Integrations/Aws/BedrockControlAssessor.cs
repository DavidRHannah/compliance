using System.ComponentModel.DataAnnotations;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using App.Data.Entities;
using App.Integrations.Aws.Prompts;
using App.Integrations.Abstractions;
using Microsoft.Extensions.Options;
using Document = Amazon.Runtime.Documents.Document;

namespace App.Integrations.Aws;

public sealed class BedrockSettings
{
    [Required]
    public string AssessmentModelId { get; set; } = "";

    [Required]
    public string EmbeddingModelId { get; set; } = "amazon.titan-embed-text-v2:0";
}

/// <summary>
/// Asks Claude through the Bedrock Converse API at temperature 0. The model must answer through one
/// tool, so the result is structured. Throttled calls are retried by the SDK client (adaptive retries).
/// </summary>
public sealed class BedrockControlAssessor(IAmazonBedrockRuntime bedrock, IOptions<BedrockSettings> settings) : IControlAssessor
{
    private const string ToolName = "record_assessment";

    private static readonly PromptTemplate Template = PromptTemplate.Load();

    public string ModelId => settings.Value.AssessmentModelId;

    public string PromptVersion => Template.Version;

    public async Task<AssessorResult> AssessAsync(ControlContext context, CancellationToken ct)
    {
        var (system, user) = Template.Render(context);
        var request = new ConverseRequest
        {
            ModelId = ModelId,
            System = [new SystemContentBlock { Text = system }],
            Messages = [new Message { Role = ConversationRole.User, Content = [new ContentBlock { Text = user }] }],
            InferenceConfig = new InferenceConfiguration { Temperature = 0 },
            ToolConfig = new ToolConfiguration
            {
                Tools = [new Tool { ToolSpec = new ToolSpecification
                {
                    Name = ToolName,
                    Description = "Record the preliminary verdict for the control.",
                    InputSchema = new ToolInputSchema { Json = Schema() },
                } }],
                ToolChoice = new ToolChoice { Tool = new SpecificToolChoice { Name = ToolName } },
            },
        };

        var response = await bedrock.ConverseAsync(request, ct);
        var input = response.Output?.Message?.Content?.FirstOrDefault(c => c.ToolUse?.Name == ToolName)?.ToolUse?.Input;
        return Parse(input);
    }

    /// <summary>The tool input as a result. Anything else is a failed attempt; the message holds no model text.</summary>
    private static AssessorResult Parse(Document? input)
    {
        const string invalid = "The model returned no valid result.";
        if (input is null || !input.Value.IsDictionary())
        {
            throw new AssessmentFailedException(invalid);
        }

        var fields = input.Value.AsDictionary();
        if (!fields.TryGetValue("verdict", out var verdictField) || !verdictField.IsString()
            || !TryVerdict(verdictField.AsString(), out var verdict)
            || !fields.TryGetValue("rationale", out var rationaleField) || !rationaleField.IsString())
        {
            throw new AssessmentFailedException(invalid);
        }

        var citations = new List<ModelCitation>();
        if (fields.TryGetValue("citations", out var list) && list.IsList())
        {
            foreach (var item in list.AsList())
            {
                if (item.IsDictionary()
                    && item.AsDictionary().TryGetValue("chunkId", out var id) && id.IsString()
                    && item.AsDictionary().TryGetValue("quote", out var quote) && quote.IsString())
                {
                    citations.Add(new ModelCitation(id.AsString(), quote.AsString()));
                }
            }
        }

        return new AssessorResult(verdict, rationaleField.AsString(), citations);
    }

    private static bool TryVerdict(string text, out PreliminaryVerdict verdict)
    {
        (var ok, verdict) = text switch
        {
            "preliminary_pass" => (true, PreliminaryVerdict.PreliminaryPass),
            "preliminary_fail" => (true, PreliminaryVerdict.PreliminaryFail),
            "interpretation_required" => (true, PreliminaryVerdict.InterpretationRequired),
            "insufficient_evidence" => (true, PreliminaryVerdict.InsufficientEvidence),
            _ => (false, default),
        };
        return ok;
    }

    private static Document Array(params string[] items) => new(items.Select(i => (Document)i).ToList());

    private static Document Schema()
    {
        static Document Str(string description) =>
            new Dictionary<string, Document> { ["type"] = "string", ["description"] = description };

        var citation = new Dictionary<string, Document>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, Document>
            {
                ["chunkId"] = Str("The chunk_id of the evidence block."),
                ["quote"] = Str("Text copied word for word from that chunk."),
            },
            ["required"] = Array("chunkId", "quote"),
        };

        return new Dictionary<string, Document>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, Document>
            {
                ["verdict"] = new Dictionary<string, Document>
                {
                    ["type"] = "string",
                    ["enum"] = Array("preliminary_pass", "preliminary_fail", "interpretation_required", "insufficient_evidence"),
                },
                ["rationale"] = Str("A short explanation for the consultant."),
                ["citations"] = new Dictionary<string, Document> { ["type"] = "array", ["items"] = citation },
            },
            ["required"] = Array("verdict", "rationale", "citations"),
        };
    }
}
