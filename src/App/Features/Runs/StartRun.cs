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
/// A consultant starts a run (AS-1). It queues one message per applicable control. The engagement must
/// be active (EN-2), have no active run (AS-14), and every applicable control needs active criteria (CT-4).
/// </summary>
public static class StartRun
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/engagements/{engagementId:guid}/runs", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(
        AccessContext access, AppDbContext db, IQueue queue, IControlAssessor assessor, HttpContext http, CancellationToken ct)
    {
        var engagementId = access.EngagementId;
        var (transaction, status) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Share, ct);
        await using var _ = transaction;

        if (status == EngagementStatus.Closed)
        {
            return Problems.Conflict("engagement-closed");
        }

        if (status == EngagementStatus.Draft)
        {
            return Problems.Conflict("engagement-not-active", "A run can start only while the engagement is active.");
        }

        if (await db.AssessmentRuns.AnyAsync(r =>
                r.EngagementId == engagementId && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running), ct))
        {
            return Problems.Conflict("run-active");
        }

        // Each applicable control with the criteria version active now (CT-3).
        var controls = await (
                from ec in db.EngagementControls.AsNoTracking()
                where ec.EngagementId == engagementId && ec.IsApplicable
                join c in db.Controls on ec.ControlId equals c.Id
                join k in db.AssessmentCriteria.Where(k => k.IsActive) on c.Id equals k.ControlId into active
                from k in active.DefaultIfEmpty()
                orderby c.Id
                select new { c.Id, c.RefCode, CriteriaId = (Guid?)k.Id })
            .ToListAsync(ct);

        var missing = controls.Where(c => c.CriteriaId is null).Select(c => c.RefCode).ToList();
        if (missing.Count > 0)
        {
            return Problems.Conflict("criteria-missing", "An applicable control has no active assessment criteria.",
                new Dictionary<string, object?> { ["controls"] = missing });
        }

        var now = DateTimeOffset.UtcNow;
        var run = new AssessmentRun
        {
            Id = Guid.CreateVersion7(),
            EngagementId = engagementId,
            TriggeredById = access.Caller.Id,
            ModelId = assessor.ModelId,
            PromptVersion = assessor.PromptVersion,
            Status = controls.Count == 0 ? RunStatus.Completed : RunStatus.Queued,
            CompletedAt = controls.Count == 0 ? now : null,
            CreatedAt = now,
        };
        db.AssessmentRuns.Add(run);
        db.AssessmentRunControls.AddRange(controls.Select(c => new AssessmentRunControl
        {
            RunId = run.Id,
            ControlId = c.Id,
            AssessmentCriteriaId = c.CriteriaId!.Value,
        }));
        db.AddAudit(access.Caller.Id, engagementId, "run.started", "assessment_run", run.Id,
            new { run.ModelId, run.PromptVersion, controls = controls.Count });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (Problems.IsUniqueViolation(e, "ix_assessment_runs_one_active"))
        {
            return Problems.Conflict("run-active");
        }

        // Sent before the commit (decision 16): a worker may find nothing yet and the message comes back.
        var correlationId = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        foreach (var control in controls)
        {
            await queue.SendAsync(Queues.Assessments, new AssessControlMessage(run.Id, engagementId, control.Id, correlationId), ct);
        }

        await transaction.CommitAsync(ct);

        var response = new RunResponse(run.Id, run.Status, run.ModelId, run.PromptVersion, run.TriggeredById,
            run.CreatedAt, run.StartedAt, run.CompletedAt, new RunProgress(controls.Count, 0, 0));
        return Results.Accepted($"/api/engagements/{engagementId}/runs/{run.Id}", response);
    }
}
