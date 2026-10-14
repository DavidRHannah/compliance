using App.Common.Access;
using App.Common.Audit;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Decisions;

/// <summary>
/// A consultant accepts or overrides an assessment's verdict. The assessment is locked, so a decision
/// and a run that replaces it never overlap (RV-11). A decision clears the assessment's open flags (RV-12).
/// </summary>
public static class RecordDecision
{
    public const int MaxNotesLength = 4000;

    private static readonly FinalVerdict[] Allowed = [FinalVerdict.Pass, FinalVerdict.Fail, FinalVerdict.InsufficientEvidence];

    public sealed record Request(string? FinalVerdict, string? Notes);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/engagements/{engagementId:guid}/assessments/{assessmentId:guid}/decisions", Handle)
            .AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(
        Guid assessmentId, Request request, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        // Only pass, fail and insufficient evidence are final verdicts (RV-4).
        var final = EnumQuery.Parse<FinalVerdict>(request.FinalVerdict);
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var errors = new Dictionary<string, string[]>();
        if (final is null || !Allowed.Contains(final.Value))
        {
            errors["finalVerdict"] = [$"Must be one of: {EnumQuery.Names(Allowed)}."];
        }

        if (notes is { Length: > MaxNotesLength })
        {
            errors["notes"] = [$"At most {MaxNotesLength} characters."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var engagementId = access.EngagementId;
        var (transaction, status) = await EngagementWrite.BeginAsync(db, engagementId, EngagementLock.Share, ct);
        await using var _ = transaction;

        var assessment = (await db.Assessments
                .FromSql($"""
                    SELECT * FROM assessments
                    WHERE id = {assessmentId} AND engagement_id = {engagementId}
                    FOR SHARE
                    """)
                .AsNoTracking()
                .ToListAsync(ct))
            .SingleOrDefault();
        if (assessment is null)
        {
            return Problems.NotFound();
        }

        if (status == EngagementStatus.Closed)
        {
            return Problems.Conflict("engagement-closed");
        }

        if (!assessment.IsCurrent)
        {
            var currentId = await db.Assessments
                .Where(a => a.EngagementId == engagementId && a.ControlId == assessment.ControlId && a.ScopeItemId == null && a.IsCurrent)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            return Problems.Conflict("assessment-not-current", "A newer assessment replaced this one.",
                new Dictionary<string, object?> { ["currentAssessmentId"] = currentId });
        }

        var accepted = DecisionRules.IsAccept(assessment.Verdict, final!.Value);
        if (!accepted && notes is null)
        {
            return Problems.BadRequest("note-required", "An override needs a note.");
        }

        var caller = access.Caller.Id;
        var now = DateTimeOffset.UtcNow;
        var cleared = await db.ReviewFlags
            .Where(f => f.AssessmentId == assessmentId && f.EngagementId == engagementId && f.ClearedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ClearedAt, now).SetProperty(f => f.ClearedById, caller), ct);

        var decision = new ConsultantDecision
        {
            Id = Guid.CreateVersion7(),
            AssessmentId = assessmentId,
            DecidedById = caller,
            FinalVerdict = final.Value,
            Notes = notes,
        };
        db.ConsultantDecisions.Add(decision);
        // The note is not copied into the audit event.
        db.AddAudit(caller, engagementId, "decision.recorded", "consultant_decision", decision.Id,
            new { assessmentId, finalVerdict = final.Value, accepted, flagsCleared = cleared });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var name = await db.Users.Where(u => u.Id == caller).Select(u => u.DisplayName).SingleAsync(ct);
        var response = new DecisionResponse(decision.Id, assessmentId, decision.FinalVerdict, decision.Notes, caller, name, decision.DecidedAt, accepted);
        return Results.Created($"/api/engagements/{engagementId}/assessments/{assessmentId}", response);
    }
}
