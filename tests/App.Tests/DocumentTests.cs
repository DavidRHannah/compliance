using App.Data.Entities;
using App.Pipeline.Assessments;
using App.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace App.Tests;

[Collection(PostgresCollection.Name)]
public class DocumentTests(PostgresFixture postgres)
{
    [Fact(Skip = "Pending")]
    [Requirement("DO-1", "A user uploads a file directly to storage through a time-limited upload link. The backend does not receive the file's contents in the request.")]
    public void Upload_GoesDirectlyToStorage()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-2", "The backend accepts PDF, DOCX, XLSX, CSV and PPTX files, and rejects all other types and any file over 50 MB. The size limit is configurable.")]
    public void Upload_RejectsWrongTypeOrOversize()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-3", "A file identical to one in the engagement that is not deleted is not processed again. It ends as failed, with the reason duplicate.")]
    public void DuplicateFile_FailsAsDuplicate()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-4", "A stored file is never modified. Replacing a document stores a new file.")]
    public void StoredFile_IsNeverModified()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-5", "Each document has a status: pending, processing, ready or failed. A failed document shows the reason.")]
    public void Document_HasStatusAndFailureReason()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-6", "An authorized user can download an original file through a time-limited link.")]
    public void Download_UsesTimeLimitedLink()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-7", "A user who may upload documents may delete a document or replace it with a new version.")]
    public void Uploader_CanDeleteOrReplace()
    {
    }

    [Fact]
    [Requirement("DO-8", "A deleted or replaced document is excluded from search and from later runs.")]
    public async Task DeletedDocument_IsExcludedFromSearchAndRuns()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var queries = new[] { "information security policy" };
        var embedder = new FakeEmbedder();
        Task<List<Guid>> SearchAsync() => EvidenceSearch.FindAsync(s.Db, embedder, s.Engagement.Id, queries, CancellationToken.None);
        Assert.Equal([s.Chunk.Id], await SearchAsync());

        // A superseded chunk (re-chunking) is left out.
        await s.Db.DocumentChunks.Where(c => c.Id == s.Chunk.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.SupersededAt, DateTimeOffset.UtcNow));
        Assert.Empty(await SearchAsync());
        await s.Db.DocumentChunks.Where(c => c.Id == s.Chunk.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.SupersededAt, (DateTimeOffset?)null));
        Assert.Equal([s.Chunk.Id], await SearchAsync());

        // A deleted or replaced document is retired with deleted_at, and a trigger hides its chunks.
        await s.Db.Documents.Where(d => d.Id == s.Document.Id).ExecuteUpdateAsync(u => u.SetProperty(d => d.DeletedAt, DateTimeOffset.UtcNow));
        Assert.Empty(await SearchAsync());

        // A later run finds no evidence, so it does not call the model.
        await s.StartAndAssessAsync();
        Assert.Empty(s.Factory.Assessor.Contexts);
        var assessment = Assert.Single(await s.Db.Assessments.AsNoTracking().Where(a => a.EngagementId == s.Engagement.Id).ToListAsync());
        Assert.Equal(PreliminaryVerdict.InsufficientEvidence, assessment.Verdict);
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-9", "A file cannot be changed after its upload is confirmed. Processing and downloads always use the confirmed version.")]
    public void ConfirmedVersion_IsUsedForProcessingAndDownload()
    {
    }
}
