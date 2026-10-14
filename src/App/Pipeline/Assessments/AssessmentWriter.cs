using App.Common.Access;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Pipeline.Assessments;

/// <summary>What the worker decided for one control, ready to save.</summary>
public sealed record AssessmentToSave(
    Guid RunId, Guid EngagementId, Guid ControlId,
    PreliminaryVerdict Verdict, string Rationale, IReadOnlyList<CheckedCitation> Citations);

/// <summary>Saves an assessment, or a control's failure, and finishes the run when it was the last control.</summary>
public sealed class AssessmentWriter(AppDbContext db)
{
    public const int MaxErrorLength = 200;

    /// <summary>
    /// One transaction, in this order: lock and un-current the earlier assessment, read its decisions,
    /// lock the cited chunks and drop citations to deleted ones, apply AS-7 again, insert the assessment
    /// and its citations, flag it if the earlier one had a decision (RV-7), then check the run.
    /// Returns false when another delivery of the same message saved first (QR-5).
    /// </summary>
    public async Task<bool> SaveAsync(AssessmentToSave toSave, CancellationToken ct)
    {
        var (transaction, _) = await EngagementWrite.BeginAsync(db, toSave.EngagementId, EngagementLock.Share, ct);
        await using var _ = transaction;

        var earlier = (await db.Assessments
                .FromSql($"""
                    SELECT * FROM assessments
                    WHERE engagement_id = {toSave.EngagementId} AND control_id = {toSave.ControlId}
                      AND scope_item_id IS NULL AND is_current
                    FOR UPDATE
                    """)
                .ToListAsync(ct))
            .SingleOrDefault();

        if (earlier?.RunId == toSave.RunId
            || await db.Assessments.AnyAsync(a => a.RunId == toSave.RunId && a.ControlId == toSave.ControlId, ct))
        {
            return false;
        }

        var hadDecision = false;
        if (earlier is not null)
        {
            hadDecision = await db.ConsultantDecisions.AnyAsync(d => d.AssessmentId == earlier.Id, ct);
            earlier.IsCurrent = false;

            // The one-current index allows one current row, so the old row is cleared before the new one goes in.
            await db.SaveChangesAsync(ct);
        }

        // Cited chunks that still exist in this engagement, locked so a delete waits for this save.
        var wanted = toSave.Citations.Where(c => c.ChunkId is not null).Select(c => c.ChunkId!.Value).Distinct().ToArray();
        var live = wanted.Length == 0
            ? []
            : (await db.Database.SqlQuery<Guid>($"""
                    SELECT id AS "Value" FROM document_chunks
                    WHERE engagement_id = {toSave.EngagementId} AND id = ANY({wanted}) AND deleted_at IS NULL
                    FOR SHARE
                    """)
                .ToListAsync(ct)).ToHashSet();
        var kept = toSave.Citations.Where(c => c.ChunkId is { } id && live.Contains(id)).ToList();

        var verdict = VerdictRules.Apply(toSave.Verdict, kept.Count(c => c.IsVerified));
        var now = DateTimeOffset.UtcNow;
        var assessment = new Assessment
        {
            Id = Guid.CreateVersion7(),
            RunId = toSave.RunId,
            EngagementId = toSave.EngagementId,
            ControlId = toSave.ControlId,
            Verdict = verdict,
            Rationale = toSave.Rationale,
            CreatedAt = now,
        };
        db.Assessments.Add(assessment);
        db.EvidenceCitations.AddRange(kept.Select(c => new EvidenceCitation
        {
            Id = Guid.CreateVersion7(),
            AssessmentId = assessment.Id,
            ChunkId = c.ChunkId!.Value,
            EngagementId = toSave.EngagementId,
            QuotedText = c.Quote,
            CharStart = c.CharStart,
            CharEnd = c.CharEnd,
            IsVerified = c.IsVerified,
        }));

        if (earlier is not null && hadDecision)
        {
            db.ReviewFlags.Add(new ReviewFlag
            {
                Id = Guid.CreateVersion7(),
                AssessmentId = assessment.Id,
                EngagementId = toSave.EngagementId,
                Reason = ReviewFlagReason.DecisionSuperseded,
                SupersededAssessmentId = earlier.Id,
                CreatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (App.Common.Errors.Problems.IsUniqueViolation(e, "ix_assessments_run_id_control_id"))
        {
            // Another delivery of the same message saved first; this transaction rolls back.
            return false;
        }

        await FinishRunIfDoneAsync(toSave.RunId, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Records that a control failed its last attempt, then checks the run. A control that already
    /// has an assessment is left alone. The error text must hold no document text, quotes or prompts.
    /// </summary>
    public async Task RecordFailureAsync(Guid runId, Guid engagementId, Guid controlId, string error, CancellationToken ct)
    {
        var (transaction, _) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Share, ct);
        await using var _ = transaction;

        await LockRunAsync(runId, ct);
        var safe = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        var now = DateTimeOffset.UtcNow;
        await db.AssessmentRunControls
            .Where(rc => rc.RunId == runId && rc.ControlId == controlId && rc.FailedAt == null
                && !db.Assessments.Any(a => a.RunId == runId && a.ControlId == controlId))
            .ExecuteUpdateAsync(s => s.SetProperty(rc => rc.FailedAt, now).SetProperty(rc => rc.Error, safe), ct);

        await FinishRunIfDoneAsync(runId, ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// A run is completed when every control has an assessment or a recorded failure. If any failed,
    /// the run is failed instead, and a consultant can resume it (AS-10). The caller holds the transaction.
    /// </summary>
    public async Task FinishRunIfDoneAsync(Guid runId, CancellationToken ct)
    {
        await LockRunAsync(runId, ct);
        var total = await db.AssessmentRunControls.CountAsync(rc => rc.RunId == runId, ct);
        var failed = await db.AssessmentRunControls.CountAsync(rc => rc.RunId == runId && rc.FailedAt != null, ct);
        var assessed = await db.Assessments.CountAsync(a => a.RunId == runId, ct);
        if (failed + assessed < total)
        {
            return;
        }

        var status = failed > 0 ? RunStatus.Failed : RunStatus.Completed;
        var now = DateTimeOffset.UtcNow;
        await db.AssessmentRuns
            .Where(r => r.Id == runId && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.CompletedAt, now), ct);
    }

    private Task<List<Guid>> LockRunAsync(Guid runId, CancellationToken ct) =>
        db.Database.SqlQuery<Guid>($"""SELECT id AS "Value" FROM assessment_runs WHERE id = {runId} FOR UPDATE""").ToListAsync(ct);
}
