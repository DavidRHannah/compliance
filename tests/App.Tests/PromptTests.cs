using App.Data.Entities;
using App.Integrations.Abstractions;
using App.Integrations.Aws.Prompts;

namespace App.Tests;

public class PromptTests
{
    private static ControlContext Sample() => new(
        new ControlText("5.1", "Policies for information security", "Statement text."),
        new CriteriaText(["Information security policy", "Approval record"], "Approved policy.", "No policy.", true),
        [new ScopeItemText(ScopeItemType.System, "CRM", "Customer records")],
        [new EvidenceChunk(Guid.NewGuid(), "Policy text.", "Scope", "policy.pdf", "application/pdf", DateTimeOffset.UtcNow)]);

    [Fact]
    public void Template_LeavesNoPlaceholderUnfilled()
    {
        var template = PromptTemplate.Load();

        var (system, user) = template.Render(Sample());

        Assert.DoesNotContain("{{", system);
        Assert.DoesNotContain("{{", user);
        Assert.NotEmpty(template.Placeholders());
        Assert.Contains("Policies for information security", user);
        Assert.Contains("- Approval record", user);
        Assert.Contains("system: CRM (Customer records)", user);
    }

    [Fact]
    public void Template_FillsEmptyScopeAndEvidence()
    {
        var context = Sample() with { ScopeItems = [], Evidence = [] };

        var (_, user) = PromptTemplate.Load().Render(context);

        Assert.DoesNotContain("{{", user);
        Assert.Contains("(none defined)", user);
        Assert.Contains("(no evidence found)", user);
    }

    [Fact]
    public void Template_VersionIsItsFileName()
    {
        Assert.Equal("assess-control.v1", PromptTemplate.Load().Version);
    }
}
