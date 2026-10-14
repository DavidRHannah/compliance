using App.Data;
using App.Data.Entities;

namespace App.Features.Decisions;

public sealed record DecisionResponse(
    Guid Id, Guid AssessmentId, FinalVerdict FinalVerdict, string? Notes,
    Guid DecidedById, string DecidedByName, DateTimeOffset DecidedAt, bool Accepted);

/// <summary>A decision with the preliminary verdict it answers, so "accepted" can be worked out.</summary>
public sealed record DecisionRow(
    Guid Id, Guid AssessmentId, Guid ControlId, bool AssessmentIsCurrent, FinalVerdict FinalVerdict, string? Notes,
    Guid DecidedById, string DecidedByName, DateTimeOffset DecidedAt, PreliminaryVerdict Preliminary)
{
    public DecisionResponse ToResponse() =>
        new(Id, AssessmentId, FinalVerdict, Notes, DecidedById, DecidedByName, DecidedAt, DecisionRules.IsAccept(Preliminary, FinalVerdict));
}

public static class DecisionQueries
{
    /// <summary>Every decision in the engagement. consultant_decisions has no engagement_id, so this joins assessments.</summary>
    public static IQueryable<DecisionRow> Rows(AppDbContext db, Guid engagementId, IReadOnlyCollection<Guid>? assessmentIds = null) =>
        Project(db, engagementId, Only(db.ConsultantDecisions, assessmentIds));

    /// <summary>The decision of record for each assessment: the latest by (decided_at, id) (RV-5).</summary>
    public static IQueryable<DecisionRow> OfRecord(AppDbContext db, Guid engagementId, IReadOnlyCollection<Guid>? assessmentIds = null) =>
        Project(db, engagementId, Only(db.ConsultantDecisions, assessmentIds).Where(d => !db.ConsultantDecisions.Any(o =>
            o.AssessmentId == d.AssessmentId
            && (o.DecidedAt > d.DecidedAt || (o.DecidedAt == d.DecidedAt && o.Id.CompareTo(d.Id) > 0)))));

    private static IQueryable<ConsultantDecision> Only(IQueryable<ConsultantDecision> decisions, IReadOnlyCollection<Guid>? assessmentIds)
    {
        var ids = assessmentIds?.ToArray();
        return ids is null ? decisions : decisions.Where(d => ids.Contains(d.AssessmentId));
    }

    // Filter the decisions first and project last: EF cannot filter on a record built in a projection.
    private static IQueryable<DecisionRow> Project(AppDbContext db, Guid engagementId, IQueryable<ConsultantDecision> decisions) =>
        from d in decisions
        join a in db.Assessments on d.AssessmentId equals a.Id
        where a.EngagementId == engagementId
        join u in db.Users on d.DecidedById equals u.Id
        select new DecisionRow(
            d.Id, a.Id, a.ControlId, a.IsCurrent, d.FinalVerdict, d.Notes, u.Id, u.DisplayName, d.DecidedAt, a.Verdict);
}
