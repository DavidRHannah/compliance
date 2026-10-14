using System.Text.Json;
using App.Data;
using App.Data.Entities;

namespace App.Tests.Infrastructure;

/// <summary>
/// Inserts the smallest valid rows a test needs. Every call uses fresh names and IDs,
/// so tests never collide in the shared database.
/// </summary>
public sealed class Seed(AppDbContext db)
{
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    public async Task<User> ConsultantAsync()
    {
        var user = new User
        {
            ExternalId = Guid.NewGuid().ToString(),
            Email = $"{Guid.NewGuid()}@example.test",
            DisplayName = "Test Consultant",
            Role = UserRole.Consultant,
            CreatedAt = Now,
        };
        return await AddAsync(user);
    }

    public async Task<User> AdminAsync()
    {
        return await AddAsync(new User
        {
            ExternalId = Guid.NewGuid().ToString(),
            Email = $"{Guid.NewGuid()}@example.test",
            DisplayName = "Test Admin",
            Role = UserRole.Admin,
            CreatedAt = Now,
        });
    }

    public async Task<User> DisabledConsultantAsync()
    {
        var user = await ConsultantAsync();
        user.DisabledAt = Now;
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<Client> ClientAsync()
    {
        return await AddAsync(new Client { Name = "Test Client", CreatedAt = Now });
    }

    public async Task<User> ClientUserAsync(Client client)
    {
        return await AddAsync(new User
        {
            ExternalId = Guid.NewGuid().ToString(),
            Email = $"{Guid.NewGuid()}@example.test",
            DisplayName = "Test Client User",
            Role = UserRole.Client,
            ClientId = client.Id,
            CreatedAt = Now,
        });
    }

    public async Task<Engagement> EngagementAsync(User lead)
    {
        return await EngagementAsync(lead, await ClientAsync());
    }

    public async Task<Engagement> EngagementAsync(User lead, Client client, EngagementStatus status = EngagementStatus.Draft)
    {
        return await AddAsync(new Engagement
        {
            ClientId = client.Id,
            LeadConsultantId = lead.Id,
            Name = "Test Engagement",
            Status = status,
            CreatedAt = Now,
        });
    }

    /// <summary>
    /// The framework engagement creation attaches, with three controls. Created once and shared:
    /// the Postgres tests run one at a time, so get-or-create is safe.
    /// </summary>
    public async Task<Framework> Iso27001Async()
    {
        var existing = db.Frameworks.SingleOrDefault(f => f.Key == "iso_27001" && f.Version == "2022");
        if (existing is not null)
        {
            return existing;
        }

        var framework = await AddAsync(new Framework { Key = "iso_27001", Name = "ISO/IEC 27001", Version = "2022" });
        foreach (var refCode in new[] { "5.1", "5.2", "8.24" })
        {
            await AddAsync(new Control
            {
                FrameworkId = framework.Id,
                RefCode = refCode,
                Theme = "organizational",
                Title = $"Control {refCode}",
                Statement = "Test statement.",
            });
        }

        return framework;
    }

    public async Task<ScopeItem> ScopeItemAsync(Engagement engagement, User creator)
    {
        return await AddAsync(new ScopeItem
        {
            EngagementId = engagement.Id,
            Type = ScopeItemType.System,
            Name = $"System {Guid.NewGuid():N}",
            CreatedById = creator.Id,
            CreatedAt = Now,
        });
    }

    public async Task<Control> ControlAsync()
    {
        var framework = await AddAsync(new Framework
        {
            Key = $"test_{Guid.NewGuid():N}",
            Name = "Test Framework",
            Version = "2022",
        });
        return await AddAsync(new Control
        {
            FrameworkId = framework.Id,
            RefCode = "5.1",
            Theme = "organizational",
            Title = "Test control",
            Statement = "Test statement.",
        });
    }

    public async Task<Document> DocumentAsync(
        Engagement engagement, User uploader, DocumentStatus status = DocumentStatus.Pending, string fileName = "policy.pdf")
    {
        var id = Guid.CreateVersion7();
        return await AddAsync(new Document
        {
            Id = id,
            EngagementId = engagement.Id,
            UploadedById = uploader.Id,
            FileName = fileName,
            MimeType = "application/pdf",
            SizeBytes = 1024,
            S3Key = $"engagements/{engagement.Id}/documents/{id}",
            Status = status,
            Error = status == DocumentStatus.Failed ? "Test failure." : null,
            CreatedAt = Now,
        });
    }

    /// <summary>A chunk with an embedding from <see cref="FakeEmbedder"/>, so search can find it.</summary>
    public async Task<DocumentChunk> ChunkAsync(Document document, int index, string content)
    {
        return await AddAsync(new DocumentChunk
        {
            DocumentId = document.Id,
            EngagementId = document.EngagementId,
            ChunkIndex = index,
            Locator = JsonDocument.Parse("""{"page": 1}"""),
            Content = content,
            TokenCount = 10,
            Embedding = new Pgvector.Vector(FakeEmbedder.Embed(content)),
            EmbeddingModel = FakeEmbedder.Model,
            Chunker = "test-v1",
        });
    }

    /// <summary>Makes the control part of the engagement, with the framework it belongs to.</summary>
    public async Task EngagementControlAsync(Engagement engagement, Control control, bool applicable = true)
    {
        if (!db.EngagementFrameworks.Any(f => f.EngagementId == engagement.Id && f.FrameworkId == control.FrameworkId))
        {
            await AddAsync(new EngagementFramework { EngagementId = engagement.Id, FrameworkId = control.FrameworkId });
        }

        await AddAsync(new EngagementControl
        {
            EngagementId = engagement.Id,
            ControlId = control.Id,
            FrameworkId = control.FrameworkId,
            IsApplicable = applicable,
            Justification = applicable ? null : "Not applicable in this test.",
        });
    }

    public async Task<AssessmentCriterion> CriteriaAsync(
        Control control, User author, bool active = true, int version = 1, params string[] expectedEvidence)
    {
        return await AddAsync(new AssessmentCriterion
        {
            ControlId = control.Id,
            Version = version,
            ExpectedEvidence = expectedEvidence.Length > 0 ? [.. expectedEvidence] : ["Information security policy"],
            PassConditions = "A current, approved policy.",
            FailConditions = "No policy.",
            IsActive = active,
            CreatedById = author.Id,
            CreatedAt = Now,
        });
    }

    public async Task<DocumentChunk> ChunkAsync(Document document, int index)
    {
        return await AddAsync(new DocumentChunk
        {
            DocumentId = document.Id,
            EngagementId = document.EngagementId,
            ChunkIndex = index,
            Locator = JsonDocument.Parse("""{"page": 1}"""),
            Content = "The information security policy is reviewed every year.",
            TokenCount = 10,
            Chunker = "test-v1",
        });
    }

    public async Task<AssessmentRun> RunAsync(Engagement engagement, User triggeredBy, RunStatus status = RunStatus.Queued)
    {
        return await AddAsync(new AssessmentRun
        {
            EngagementId = engagement.Id,
            TriggeredById = triggeredBy.Id,
            ModelId = "test-model",
            PromptVersion = "test.v1",
            Status = status,
            CreatedAt = Now,
        });
    }

    /// <summary>An assessment, with the criteria version and run row it needs.</summary>
    public async Task<Assessment> AssessmentAsync(AssessmentRun run, Control control, User author, ScopeItem? scopeItem = null)
    {
        var criteria = await AddAsync(new AssessmentCriterion
        {
            ControlId = control.Id,
            Version = 1,
            ExpectedEvidence = ["Information security policy"],
            PassConditions = "A current, approved policy.",
            FailConditions = "No policy.",
            CreatedById = author.Id,
            CreatedAt = Now,
        });
        await AddAsync(new AssessmentRunControl
        {
            RunId = run.Id,
            ControlId = control.Id,
            AssessmentCriteriaId = criteria.Id,
        });
        return await AddAsync(new Assessment
        {
            RunId = run.Id,
            EngagementId = run.EngagementId,
            ControlId = control.Id,
            ScopeItemId = scopeItem?.Id,
            Verdict = PreliminaryVerdict.PreliminaryPass,
            Rationale = "Test rationale.",
            CreatedAt = Now,
        });
    }

    private async Task<T> AddAsync<T>(T entity) where T : class
    {
        db.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }
}
