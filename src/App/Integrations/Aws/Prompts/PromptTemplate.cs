using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using App.Integrations.Abstractions;

namespace App.Integrations.Aws.Prompts;

/// <summary>
/// The prompt for one control, built from a versioned template file (decision 7). The text above the
/// <c>&lt;!-- user --&gt;</c> line is the system prompt, the text below it is the user message.
/// </summary>
public sealed partial class PromptTemplate
{
    public const string CurrentVersion = "assess-control.v1";

    private const string Separator = "<!-- user -->";

    private readonly string system;
    private readonly string user;

    private PromptTemplate(string version, string text)
    {
        Version = version;
        var parts = text.Split(Separator);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"Prompt template {version} must hold one '{Separator}' line.");
        }

        system = parts[0].Trim();
        user = parts[1].Trim();
    }

    public string Version { get; }

    /// <summary>Loads a template that is embedded in the assembly, for example assess-control.v1.</summary>
    public static PromptTemplate Load(string version = CurrentVersion)
    {
        var assembly = typeof(PromptTemplate).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith($"{version}.md", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return new PromptTemplate(version, reader.ReadToEnd());
    }

    public (string System, string User) Render(ControlContext context)
    {
        var values = new Dictionary<string, string>
        {
            ["control_ref"] = context.Control.RefCode,
            ["control_title"] = context.Control.Title,
            ["control_statement"] = context.Control.Statement,
            ["expected_evidence"] = Lines(context.Criteria.ExpectedEvidence, "(none given)"),
            ["pass_conditions"] = context.Criteria.PassConditions,
            ["fail_conditions"] = context.Criteria.FailConditions,
            ["requires_operation_evidence"] = context.Criteria.RequiresOperationEvidence ? "yes" : "no",
            ["scope_items"] = Lines(
                context.ScopeItems.Select(s =>
                    $"{s.Type.ToString().ToLowerInvariant()}: {s.Name}" + (string.IsNullOrWhiteSpace(s.Description) ? "" : $" ({s.Description})")),
                "(none defined)"),
            ["evidence"] = Evidence(context.Evidence),
        };

        // One pass over the template: text inside a value is never read as a placeholder.
        var rendered = Placeholder().Replace(user, match =>
            values.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new InvalidOperationException($"Prompt template {Version} has no value for {{{{{match.Groups[1].Value}}}}}."));
        return (system, rendered);
    }

    /// <summary>Every placeholder the user part of the template uses.</summary>
    public IReadOnlyList<string> Placeholders() =>
        Placeholder().Matches(user).Select(m => m.Groups[1].Value).Distinct().ToList();

    private static string Lines(IEnumerable<string> items, string whenEmpty)
    {
        var list = items.Select(i => $"- {i}").ToList();
        return list.Count == 0 ? whenEmpty : string.Join('\n', list);
    }

    /// <summary>
    /// Each chunk goes in its own block with its ID and its document's name, type and upload date (AS-12).
    /// A tag inside the text is made harmless, so a document cannot close its own block.
    /// </summary>
    private static string Evidence(IReadOnlyList<EvidenceChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return "(no evidence found)";
        }

        var text = new StringBuilder();
        foreach (var chunk in chunks)
        {
            text.Append($"<evidence chunk_id=\"{chunk.ChunkId}\" document=\"{WebUtility.HtmlEncode(chunk.DocumentName)}\" ")
                .Append($"type=\"{WebUtility.HtmlEncode(chunk.MimeType)}\" uploaded=\"{chunk.UploadedAt:yyyy-MM-dd}\"");
            if (!string.IsNullOrWhiteSpace(chunk.SectionTitle))
            {
                text.Append($" section=\"{WebUtility.HtmlEncode(chunk.SectionTitle)}\"");
            }

            text.Append(">\n").Append(EvidenceTag().Replace(chunk.Content, "&lt;$1")).Append("\n</evidence>\n\n");
        }

        return text.ToString().TrimEnd();
    }

    [GeneratedRegex(@"\{\{([a-z_]+)\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"<(/?evidence)", RegexOptions.IgnoreCase)]
    private static partial Regex EvidenceTag();
}
