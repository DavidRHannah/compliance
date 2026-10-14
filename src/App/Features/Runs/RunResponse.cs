using System.Linq.Expressions;
using App.Data;
using App.Data.Entities;

namespace App.Features.Runs;

/// <summary>A run with its progress, which a client polls (AS-8).</summary>
public sealed record RunResponse(
    Guid Id, RunStatus Status, string ModelId, string PromptVersion, Guid TriggeredById,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, RunProgress Progress)
{
    /// <summary>
    /// Run to response, with the progress counted in the query. Filter and page the runs first and
    /// select after: EF cannot filter or order by a projected record.
    /// </summary>
    public static Expression<Func<AssessmentRun, RunResponse>> Select(AppDbContext db) =>
        r => new RunResponse(
            r.Id, r.Status, r.ModelId, r.PromptVersion, r.TriggeredById, r.CreatedAt, r.StartedAt, r.CompletedAt,
            new RunProgress(
                db.AssessmentRunControls.Count(rc => rc.RunId == r.Id),
                db.Assessments.Count(a => a.RunId == r.Id),
                db.AssessmentRunControls.Count(rc => rc.RunId == r.Id && rc.FailedAt != null)));

    /// <summary>One run of the engagement, or none.</summary>
    public static IQueryable<RunResponse> ForRun(AppDbContext db, Guid engagementId, Guid runId) =>
        db.AssessmentRuns.Where(r => r.EngagementId == engagementId && r.Id == runId).Select(Select(db));
}

/// <summary>Controls queued, assessed and failed. A run is finished when assessed plus failed equals total.</summary>
public sealed record RunProgress(int Total, int Assessed, int Failed);
