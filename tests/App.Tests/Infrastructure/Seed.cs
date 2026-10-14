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

    public async Task<Engagement> EngagementAsync(User lead, Client client)
    {
        return await AddAsync(new Engagement
        {
            ClientId = client.Id,
            LeadConsultantId = lead.Id,
            Name = "Test Engagement",
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

    public async Task<Document> DocumentAsync(Engagement engagement, User uploader)
    {
        var id = Guid.CreateVersion7();
        return await AddAsync(new Document
        {
            Id = id,
            EngagementId = engagement.Id,
            UploadedById = uploader.Id,
            FileName = "policy.pdf",
            MimeType = "application/pdf",
            SizeBytes = 1024,
            S3Key = $"engagements/{engagement.Id}/documents/{id}",
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
    public async Task<Assessment> AssessmentAsync(AssessmentRun run, Control control, User author)
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
