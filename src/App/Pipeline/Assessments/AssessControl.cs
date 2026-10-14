using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace App.Pipeline.Assessments;

/// <summary>
/// Assesses one control of a run: search, ask, verify, apply the rules, save. Safe to run twice for the
/// same message (QR-5): it does nothing when the control already has an assessment or a recorded failure.
/// Logs never hold document text, quotes or prompts.
/// </summary>
public sealed class AssessControl(
    AppDbContext db, IEmbedder embedder, IControlAssessor assessor, AssessmentWriter writer, ILogger<AssessControl> logger)
{
    public const string NoEvidenceRationale = "No evidence was found for this control.";

    public async Task HandleAsync(AssessControlMessage message, CancellationToken ct)
    {
        var runControl = await (
                from rc in db.AssessmentRunControls.AsNoTracking()
                join r in db.AssessmentRuns.AsNoTracking() on rc.RunId equals r.Id
                where rc.RunId == message.RunId && rc.ControlId == message.ControlId && r.EngagementId == message.EngagementId
                select new { rc.AssessmentCriteriaId, rc.FailedAt, r.Status })
            .SingleOrDefaultAsync(ct);

        if (runControl is null
            || runControl.Status is not (RunStatus.Queued or RunStatus.Running)
            || runControl.FailedAt is not null
            || await db.Assessments.AnyAsync(a => a.RunId == message.RunId && a.ControlId == message.ControlId, ct))
        {
            logger.LogInformation("Nothing to do for control {ControlId} of run {RunId}", message.ControlId, message.RunId);
            return;
        }

        await db.AssessmentRuns
            .Where(r => r.Id == message.RunId && r.Status == RunStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Running)
                .SetProperty(r => r.StartedAt, DateTimeOffset.UtcNow), ct);

        // The criteria version recorded when the run started, not the one active now (CT-3).
        var control = await db.Controls.AsNoTracking().SingleAsync(c => c.Id == message.ControlId, ct);
        var criteria = await db.AssessmentCriteria.AsNoTracking()
            .SingleAsync(c => c.Id == runControl.AssessmentCriteriaId && c.ControlId == message.ControlId, ct);
        var scopeItems = await db.ScopeItems.AsNoTracking()
            .Where(s => s.EngagementId == message.EngagementId)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var queries = new List<string> { control.Title };
        queries.AddRange(criteria.ExpectedEvidence);
        queries.AddRange(scopeItems.Select(s => s.Name));

        var chunkIds = await EvidenceSearch.FindAsync(db, embedder, message.EngagementId, queries, ct);
        var evidence = await LoadEvidenceAsync(message.EngagementId, chunkIds, ct);

        AssessmentToSave toSave;
        if (evidence.Count == 0)
        {
            // Nothing to show the model, so it is not called.
            toSave = new AssessmentToSave(message.RunId, message.EngagementId, message.ControlId,
                PreliminaryVerdict.InsufficientEvidence, NoEvidenceRationale, []);
        }
        else
        {
            var context = new ControlContext(
                new ControlText(control.RefCode, control.Title, control.Statement),
                new CriteriaText(criteria.ExpectedEvidence, criteria.PassConditions, criteria.FailConditions, criteria.RequiresOperationEvidence),
                scopeItems.Select(s => new ScopeItemText(s.Type, s.Name, s.Description)).ToList(),
                evidence);
            var result = await assessor.AssessAsync(context, ct);

            var sent = evidence.ToDictionary(e => e.ChunkId, e => e.Content);
            var citations = result.Citations.Select(c => CitationCheck.Check(c, sent)).ToList();
            var verdict = VerdictRules.Apply(result.Verdict, citations.Count(c => c.IsVerified));
            toSave = new AssessmentToSave(message.RunId, message.EngagementId, message.ControlId, verdict, result.Rationale, citations);
        }

        var saved = await writer.SaveAsync(toSave, ct);
        logger.LogInformation("Assessed control {ControlId} of run {RunId} as {Verdict} with {Chunks} chunks (saved: {Saved})",
            message.ControlId, message.RunId, toSave.Verdict, evidence.Count, saved);
    }

    /// <summary>The chunks with their document's name, type and upload date, in search order (AS-12).</summary>
    private async Task<List<EvidenceChunk>> LoadEvidenceAsync(Guid engagementId, List<Guid> chunkIds, CancellationToken ct)
    {
        var rows = await (
                from c in db.DocumentChunks.AsNoTracking()
                join d in db.Documents.AsNoTracking() on new { c.DocumentId, c.EngagementId } equals new { DocumentId = d.Id, d.EngagementId }
                where c.EngagementId == engagementId && chunkIds.Contains(c.Id) && c.DeletedAt == null
                select new EvidenceChunk(c.Id, c.Content, c.SectionTitle, d.FileName, d.MimeType, d.ConfirmedAt ?? d.CreatedAt))
            .ToListAsync(ct);
        return rows.OrderBy(r => chunkIds.IndexOf(r.ChunkId)).ToList();
    }
}
