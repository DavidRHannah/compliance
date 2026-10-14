using System.Diagnostics;
using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Runs;

/// <summary>
/// Resumes a failed run. Only the engagement's latest run can be resumed (AS-15), and only the controls
/// with no assessment are queued again (AS-10).
/// </summary>
public static class ResumeRun
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/engagements/{engagementId:guid}/runs/{runId:guid}/resume", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(
        Guid runId, AccessContext access, AppDbContext db, IQueue queue, HttpContext http, CancellationToken ct)
    {
        var engagementId = access.EngagementId;
        var (transaction, status) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Share, ct);
        await using var _ = transaction;

        var run = await db.AssessmentRuns.SingleOrDefaultAsync(r => r.Id == runId && r.EngagementId == engagementId, ct);
        if (run is null)
        {
            return Problems.NotFound();
        }

        if (status == EngagementStatus.Closed)
        {
            return Problems.Conflict("engagement-closed");
        }

        var latestId = await db.AssessmentRuns
            .Where(r => r.EngagementId == engagementId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => r.Id)
            .FirstAsync(ct);
        if (latestId != run.Id)
        {
            return Problems.Conflict("run-superseded", "Only the engagement's latest run can be resumed.");
        }

        if (run.Status != RunStatus.Failed)
        {
            return Problems.Conflict("run-not-failed");
        }

        // The controls with no assessment in this run. Their earlier failure is cleared.
        var unassessed = db.AssessmentRunControls.Where(rc =>
            rc.RunId == run.Id && !db.Assessments.Any(a => a.RunId == rc.RunId && a.ControlId == rc.ControlId));
        var pending = await unassessed.OrderBy(rc => rc.ControlId).Select(rc => rc.ControlId).ToListAsync(ct);
        await unassessed.ExecuteUpdateAsync(s => s.SetProperty(rc => rc.FailedAt, (DateTimeOffset?)null).SetProperty(rc => rc.Error, (string?)null), ct);

        run.Status = pending.Count == 0 ? RunStatus.Completed : RunStatus.Queued;
        run.CompletedAt = pending.Count == 0 ? DateTimeOffset.UtcNow : null;
        db.AddAudit(access.Caller.Id, engagementId, "run.resumed", "assessment_run", run.Id, new { controls = pending.Count });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (Problems.IsUniqueViolation(e, "ix_assessment_runs_one_active"))
        {
            return Problems.Conflict("run-active");
        }

        var correlationId = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        foreach (var controlId in pending)
        {
            await queue.SendAsync(Queues.Assessments, new AssessControlMessage(run.Id, engagementId, controlId, correlationId), ct);
        }

        await transaction.CommitAsync(ct);

        var response = await RunResponse.ForRun(db, engagementId, run.Id).SingleAsync(ct);
        return Results.Accepted($"/api/engagements/{engagementId}/runs/{run.Id}", response);
    }
}
