using System.Net;
using App.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace App.Tests;

[Collection(PostgresCollection.Name)]
public class ControlTests(PostgresFixture postgres)
{
    [Fact(Skip = "Pending")]
    [Requirement("CT-1", "Each control has assessment criteria. They state the evidence to expect, the conditions for a pass, the conditions for a fail, and whether evidence that the control operates is required.")]
    public void Criteria_HaveEvidencePassFailAndOperationFields()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("CT-2", "An admin can add a new version of a control's assessment criteria. An earlier version is never edited or deleted.")]
    public void CriteriaVersions_AreNeverEditedOrDeleted()
    {
    }

    [Fact]
    [Requirement("CT-3", "Each control has one active version of its criteria. A run uses the versions that were active when it started.")]
    public async Task Run_UsesCriteriaActiveAtStart()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var control = s.Controls[0];
        var version1 = await s.Db.AssessmentCriteria.SingleAsync(k => k.ControlId == control.Id && k.IsActive);

        // Only one version can be active at a time (a partial unique index).
        var second = await s.Seed.CriteriaAsync(control, s.Consultant, active: false, version: 2);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            s.Db.AssessmentCriteria.Where(k => k.Id == second.Id).ExecuteUpdateAsync(u => u.SetProperty(k => k.IsActive, true)));

        var runId = await s.StartRunAsync();

        // Version 2 takes over after the run started. The run still uses version 1.
        await using var other = postgres.CreateDbContext();
        await using (var transaction = await other.Database.BeginTransactionAsync())
        {
            await other.AssessmentCriteria.Where(k => k.Id == version1.Id).ExecuteUpdateAsync(u => u.SetProperty(k => k.IsActive, false));
            await other.AssessmentCriteria.Where(k => k.Id == second.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(k => k.IsActive, true).SetProperty(k => k.PassConditions, "Version two conditions."));
            await transaction.CommitAsync();
        }

        await s.Factory.HandleQueuedAssessmentsAsync();

        var row = await s.Db.AssessmentRunControls.AsNoTracking().SingleAsync(rc => rc.RunId == runId);
        Assert.Equal(version1.Id, row.AssessmentCriteriaId);
        Assert.Equal("A current, approved policy.", Assert.Single(s.Factory.Assessor.Contexts).Criteria.PassConditions);
    }

    [Fact]
    [Requirement("CT-4", "A run cannot start while an applicable control has no active criteria.")]
    public async Task Run_CannotStartWithoutActiveCriteria()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var withoutCriteria = await s.Seed.ControlAsync();
        await s.Seed.EngagementControlAsync(s.Engagement, withoutCriteria);

        var response = await s.Api.PostAsync(s.RunsUrl, null);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "criteria-missing");
        Assert.Equal(["5.1"], (await response.JsonAsync()).GetProperty("controls").EnumerateArray().Select(c => c.GetString()));
        Assert.False(await s.Db.AssessmentRuns.AnyAsync(r => r.EngagementId == s.Engagement.Id));

        // A control that is not applicable needs no criteria.
        await s.Db.EngagementControls
            .Where(ec => ec.EngagementId == s.Engagement.Id && ec.ControlId == withoutCriteria.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(ec => ec.IsApplicable, false).SetProperty(ec => ec.Justification, "Out of scope."));
        await s.StartRunAsync();
    }

    [Fact(Skip = "Pending")]
    [Requirement("CT-5", "Assessment criteria belong to the firm and apply to every engagement.")]
    public void Criteria_ApplyToEveryEngagement()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("CT-6", "The control catalog is loaded by a developer task, not through the API. A control is never deleted.")]
    public void Catalog_IsLoadedByTaskAndNeverDeleted()
    {
    }
}
