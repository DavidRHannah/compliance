using App.Data.Entities;
using App.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace App.Tests;

/// <summary>
/// Rules the database itself enforces. These back the data model rules in the
/// technical specification; the requirements they serve are proven through the API.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_MatchTheModel()
    {
        await using var db = postgres.CreateDbContext();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task NewKeys_AreUuidV7()
    {
        await using var db = postgres.CreateDbContext();

        var user = await new Seed(db).ConsultantAsync();

        Assert.Equal(7, user.Id.Version);
    }

    [Fact]
    public async Task Email_IsUniqueIgnoringCase()
    {
        await using var db = postgres.CreateDbContext();
        var user = await new Seed(db).ConsultantAsync();

        db.Users.Add(new User
        {
            ExternalId = Guid.NewGuid().ToString(),
            Email = user.Email.ToUpperInvariant(),
            DisplayName = "Duplicate",
            Role = UserRole.Consultant,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await AssertViolatesAsync(db, PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Citation_CannotPointIntoAnotherEngagement()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var mine = await seed.EngagementAsync(consultant);
        var theirs = await seed.EngagementAsync(consultant);
        var theirChunk = await seed.ChunkAsync(await seed.DocumentAsync(theirs, consultant), 0);
        var run = await seed.RunAsync(mine, consultant);
        var assessment = await seed.AssessmentAsync(run, await seed.ControlAsync(), consultant);

        db.EvidenceCitations.Add(new EvidenceCitation
        {
            AssessmentId = assessment.Id,
            ChunkId = theirChunk.Id,
            EngagementId = mine.Id,
            QuotedText = "reviewed every year",
            IsVerified = false,
        });

        await AssertViolatesAsync(db, PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task DefaultTrueColumns_StoreFalse()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var engagement = await seed.EngagementAsync(consultant);
        var control = await seed.ControlAsync();
        db.EngagementFrameworks.Add(new EngagementFramework { EngagementId = engagement.Id, FrameworkId = control.FrameworkId });
        db.EngagementControls.Add(new EngagementControl
        {
            EngagementId = engagement.Id,
            ControlId = control.Id,
            FrameworkId = control.FrameworkId,
            IsApplicable = false,
            Justification = "No physical offices.",
        });
        await db.SaveChangesAsync();
        var run = await seed.RunAsync(engagement, consultant);
        var assessment = await seed.AssessmentAsync(run, control, consultant);
        assessment.IsCurrent = false;
        await db.SaveChangesAsync();

        var applicable = await db.EngagementControls.AsNoTracking()
            .Where(c => c.EngagementId == engagement.Id && c.ControlId == control.Id)
            .Select(c => c.IsApplicable).SingleAsync();
        var current = await db.Assessments.AsNoTracking()
            .Where(a => a.EngagementId == engagement.Id && a.Id == assessment.Id)
            .Select(a => a.IsCurrent).SingleAsync();

        Assert.False(applicable);
        Assert.False(current);
    }

    [Fact]
    public async Task DeletingDocument_HidesExistingAndLaterChunks()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var engagement = await seed.EngagementAsync(consultant);
        var document = await seed.DocumentAsync(engagement, consultant);
        var before = await seed.ChunkAsync(document, 0);

        document.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var after = await seed.ChunkAsync(document, 1);

        var deletedAt = await db.Documents.AsNoTracking()
            .Where(d => d.EngagementId == engagement.Id && d.Id == document.Id)
            .Select(d => d.DeletedAt).SingleAsync();
        var chunks = await db.DocumentChunks.AsNoTracking()
            .Where(c => c.EngagementId == engagement.Id && (c.Id == before.Id || c.Id == after.Id))
            .Select(c => c.DeletedAt).ToListAsync();

        Assert.NotNull(deletedAt);
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(deletedAt, c));
    }

    [Fact]
    public async Task SecondActiveRun_IsRejected()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var engagement = await seed.EngagementAsync(consultant);
        await seed.RunAsync(engagement, consultant, RunStatus.Running);

        db.AssessmentRuns.Add(new AssessmentRun
        {
            EngagementId = engagement.Id,
            TriggeredById = consultant.Id,
            ModelId = "test-model",
            PromptVersion = "test.v1",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await AssertViolatesAsync(db, PostgresErrorCodes.UniqueViolation);
    }

    [Theory]
    [InlineData("ix_document_chunks_embedding", "USING hnsw (embedding vector_cosine_ops) WHERE ((deleted_at IS NULL) AND (superseded_at IS NULL))")]
    [InlineData("ix_document_chunks_search_vector", "USING gin (search_vector)")]
    public async Task SearchIndexes_Exist(string name, string expected)
    {
        await using var db = postgres.CreateDbContext();

        var definition = await db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = {name}")
            .SingleAsync();

        Assert.Contains(expected, definition);
    }

    private static async Task AssertViolatesAsync(DbContext db, string sqlState)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgresError = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(sqlState, postgresError.SqlState);
    }
}
