using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Data.Entities;
using App.Integrations.Abstractions;
using App.Tests.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace App.Tests;

[Collection(PostgresCollection.Name)]
public class ReviewTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> DecideAsync(RunScenario s, Guid assessmentId, string? verdict, string? notes = null) =>
        s.Api.PostAsJsonAsync($"{s.AssessmentsUrl}/{assessmentId}/decisions", new { finalVerdict = verdict, notes });

    private static async Task<Assessment> AssessOnceAsync(RunScenario s) =>
        (await s.AssessmentsAsync(await s.StartAndAssessAsync())).Single();

    private async Task<int> DecisionCountAsync(RunScenario s)
    {
        await using var db = postgres.CreateDbContext();
        return await db.ConsultantDecisions.CountAsync(d => db.Assessments.Any(a => a.Id == d.AssessmentId && a.EngagementId == s.Engagement.Id));
    }

    /// <summary>A decided assessment that a second run replaced, so the new one is flagged. Returns (old, new).</summary>
    private static async Task<(Assessment Old, Assessment New)> DecidedThenRerunAsync(RunScenario s)
    {
        var old = await AssessOnceAsync(s);
        await (await DecideAsync(s, old.Id, "pass")).AssertStatusAsync(HttpStatusCode.Created);
        var current = await AssessOnceAsync(s);
        return (old, current);
    }

    [Fact]
    [Requirement("RV-1", "A consultant can view each current assessment with its verdict, rationale, citations, and the source location of each citation.")]
    public async Task Consultant_ViewsAssessmentWithCitationLocations()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var assessment = await AssessOnceAsync(s);

        var list = await s.Api.GetJsonAsync(s.AssessmentsUrl);
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(assessment.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("preliminary_pass", item.GetProperty("verdict").GetString());

        var detail = await s.Api.GetJsonAsync($"{s.AssessmentsUrl}/{assessment.Id}");
        Assert.NotEmpty(detail.GetProperty("rationale").GetString()!);
        var citation = Assert.Single(detail.GetProperty("citations").EnumerateArray());
        Assert.Equal(s.Chunk.Id, citation.GetProperty("chunkId").GetGuid());
        Assert.Equal("policy.pdf", citation.GetProperty("documentName").GetString());
        Assert.Equal(1, citation.GetProperty("locator").GetProperty("page").GetInt32());
        Assert.NotEmpty(citation.GetProperty("quotedText").GetString()!);
    }

    [Fact]
    [Requirement("RV-2", "A consultant records a decision on an assessment: accept the verdict, or override it with a different one. Accepting a preliminary pass makes the final verdict pass, accepting a preliminary fail makes it fail, and accepting insufficient evidence keeps it. Interpretation required cannot be accepted, only overridden.")]
    public async Task Decision_AcceptsOrOverrides()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var passed = await AssessOnceAsync(s);

        var accept = await (await DecideAsync(s, passed.Id, "pass")).JsonAsync();
        Assert.True(accept.GetProperty("accepted").GetBoolean());
        Assert.Equal("pass", accept.GetProperty("finalVerdict").GetString());

        var overridden = await DecideAsync(s, passed.Id, "fail", "The policy is not approved.");
        await overridden.AssertStatusAsync(HttpStatusCode.Created);
        Assert.False((await overridden.JsonAsync()).GetProperty("accepted").GetBoolean());

        // Interpretation required has no matching final verdict, so every decision on it is an override.
        s.Factory.Assessor.Respond = _ => new AssessorResult(PreliminaryVerdict.InterpretationRequired, "Unclear.", []);
        var unclear = await AssessOnceAsync(s);
        await (await DecideAsync(s, unclear.Id, "pass")).AssertProblemAsync(HttpStatusCode.BadRequest, "note-required");
        var viaNote = await DecideAsync(s, unclear.Id, "pass", "Reviewed by hand.");
        await viaNote.AssertStatusAsync(HttpStatusCode.Created);
        Assert.False((await viaNote.JsonAsync()).GetProperty("accepted").GetBoolean());
    }

    [Fact]
    [Requirement("RV-3", "An override must include a note.")]
    public async Task Override_RequiresNote()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var assessment = await AssessOnceAsync(s);

        await (await DecideAsync(s, assessment.Id, "fail")).AssertProblemAsync(HttpStatusCode.BadRequest, "note-required");
        await (await DecideAsync(s, assessment.Id, "fail", "   ")).AssertProblemAsync(HttpStatusCode.BadRequest, "note-required");

        Assert.Equal(0, await DecisionCountAsync(s));
    }

    [Fact]
    [Requirement("RV-4", "A consultant's final verdict is one of: pass, fail or insufficient evidence. Not applicable comes only from applicability (EN-4).")]
    public async Task FinalVerdict_IsPassFailOrInsufficientEvidence()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var assessment = await AssessOnceAsync(s);

        foreach (var bad in new string?[] { "not_applicable", "preliminary_pass", "interpretation_required", "0", "", null })
        {
            await (await DecideAsync(s, assessment.Id, bad, "note")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        Assert.Equal(0, await DecisionCountAsync(s));
        await (await DecideAsync(s, assessment.Id, "insufficient_evidence", "Not enough.")).AssertStatusAsync(HttpStatusCode.Created);
    }

    [Fact]
    [Requirement("RV-5", "A decision is never edited or deleted. A later decision on the same assessment becomes the decision of record.")]
    public async Task LatestDecision_IsDecisionOfRecord()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var assessment = await AssessOnceAsync(s);
        var first = await (await DecideAsync(s, assessment.Id, "pass")).JsonAsync();
        var second = await (await DecideAsync(s, assessment.Id, "fail", "Changed my mind.")).JsonAsync();

        var detail = await s.Api.GetJsonAsync($"{s.AssessmentsUrl}/{assessment.Id}");
        var decisions = detail.GetProperty("decisions").EnumerateArray().ToList();
        Assert.Equal([second.GetProperty("id").GetGuid(), first.GetProperty("id").GetGuid()], decisions.Select(d => d.GetProperty("id").GetGuid()));

        var list = await s.Api.GetJsonAsync(s.AssessmentsUrl);
        var item = list.GetProperty("items")[0];
        Assert.Equal("fail", item.GetProperty("decision").GetProperty("finalVerdict").GetString());

        // The first decision is unchanged.
        await using var db = postgres.CreateDbContext();
        var stored = await db.ConsultantDecisions.AsNoTracking().SingleAsync(d => d.Id == first.GetProperty("id").GetGuid());
        Assert.Equal(FinalVerdict.Pass, stored.FinalVerdict);
        Assert.Null(stored.Notes);
    }

    [Fact(Skip = "Pending: uploads, scope changes and applicability are audited, and uploads do not exist yet")]
    [Requirement("RV-6", "The backend records who performed each upload, scope change, applicability change, run and decision, and when.")]
    public void Changes_AreRecordedWithActorAndTime()
    {
    }

    [Fact]
    [Requirement("RV-7", "When a new run supersedes an assessment that has a decision, the new assessment is flagged for re-review and shows the earlier decision.")]
    public async Task SupersededDecidedAssessment_IsFlagged()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var (old, current) = await DecidedThenRerunAsync(s);

        var detail = await s.Api.GetJsonAsync($"{s.AssessmentsUrl}/{current.Id}");
        var flag = Assert.Single(detail.GetProperty("flags").EnumerateArray());
        Assert.Equal("decision_superseded", flag.GetProperty("reason").GetString());
        Assert.Equal(old.Id, flag.GetProperty("supersededAssessmentId").GetGuid());
        Assert.Equal("pass", flag.GetProperty("earlierDecision").GetProperty("finalVerdict").GetString());

        var flags = await s.Api.GetJsonAsync($"{s.FlagsUrl}?reason=decision_superseded");
        Assert.Equal(current.Id, Assert.Single(flags.GetProperty("items").EnumerateArray()).GetProperty("assessmentId").GetGuid());
        var none = await s.Api.GetJsonAsync($"{s.FlagsUrl}?reason=document_deleted");
        Assert.Empty(none.GetProperty("items").EnumerateArray());
        await (await s.Api.GetAsync($"{s.FlagsUrl}?reason=nonsense")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
    }

    [Fact(Skip = "Pending: document delete and replace")]
    [Requirement("RV-8", "When a cited document is deleted or replaced, each assessment that cites it is flagged for re-review, and the flag names the document.")]
    public void DeletedCitedDocument_FlagsAssessments()
    {
    }

    [Fact]
    [Requirement("RV-9", "A flag does not change a verdict or start a run. The consultant decides what to do.")]
    public async Task Flag_DoesNotChangeVerdictOrStartRun()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var (_, current) = await DecidedThenRerunAsync(s);
        var queued = s.Factory.Queue.Assessments().Count;
        var flag = Assert.Single((await s.Api.GetJsonAsync(s.FlagsUrl)).GetProperty("items").EnumerateArray());

        await (await s.Api.PostAsync($"{s.FlagsUrl}/{flag.GetProperty("id").GetGuid()}/clear", null)).AssertStatusAsync(HttpStatusCode.NoContent);

        s.Db.ChangeTracker.Clear();
        Assert.Equal(current.Verdict, (await s.Db.Assessments.AsNoTracking().SingleAsync(a => a.Id == current.Id)).Verdict);
        Assert.Equal(2, await s.Db.AssessmentRuns.CountAsync(r => r.EngagementId == s.Engagement.Id));
        Assert.Equal(queued, s.Factory.Queue.Assessments().Count);
    }

    [Fact]
    [Requirement("RV-10", "A consultant can export an engagement's current verdicts, rationales, citations and decisions as an XLSX file.")]
    public async Task Export_ProducesXlsx()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var assessment = await AssessOnceAsync(s);
        await (await DecideAsync(s, assessment.Id, "pass")).AssertStatusAsync(HttpStatusCode.Created);

        // A formula-looking quote stays text, and an unverified quote never reaches the file.
        var chunk = await s.Seed.ChunkAsync(s.Document, 1, "=1+1 is not a formula");
        await s.Seed.CitationAsync(assessment, chunk, "=1+1 is not a formula");
        await s.Seed.CitationAsync(assessment, chunk, "Never verified text", verified: false);

        var response = await s.Api.GetAsync(s.ExportUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);

        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var controls = workbook.Worksheet("Controls");
        Assert.Equal("preliminary_pass", controls.Cell(2, 7).GetString());
        Assert.Equal("The evidence supports the control.", controls.Cell(2, 8).GetString());
        Assert.Equal("pass", controls.Cell(2, 4).GetString());
        Assert.Equal("accepted", controls.Cell(2, 10).GetString());

        var citations = workbook.Worksheet("Citations");
        var quotes = citations.RowsUsed().Skip(1).Select(r => r.Cell(5).GetString()).ToList();
        Assert.Contains("=1+1 is not a formula", quotes);
        Assert.DoesNotContain("Never verified text", quotes);
        var formulaCell = citations.RowsUsed().Skip(1).Select(r => r.Cell(5)).Single(c => c.GetString().StartsWith('='));
        Assert.False(formulaCell.HasFormula);
        Assert.Equal(XLDataType.Text, formulaCell.DataType);
    }

    [Fact]
    [Requirement("RV-11", "A decision on an assessment that is no longer current is rejected, and the consultant is shown the current assessment.")]
    public async Task DecisionOnNonCurrentAssessment_IsRejected()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var old = await AssessOnceAsync(s);
        var current = await AssessOnceAsync(s);

        var response = await DecideAsync(s, old.Id, "pass");

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "assessment-not-current");
        Assert.Equal(current.Id, (await response.JsonAsync()).GetProperty("currentAssessmentId").GetGuid());
        Assert.Equal(0, await DecisionCountAsync(s));
    }

    [Fact]
    [Requirement("RV-12", "Recording a decision clears the assessment's open flags. A consultant can also clear a flag without recording a decision.")]
    public async Task Decision_ClearsOpenFlags()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var (_, current) = await DecidedThenRerunAsync(s);
        Assert.Single((await s.Api.GetJsonAsync(s.FlagsUrl)).GetProperty("items").EnumerateArray());

        await (await DecideAsync(s, current.Id, "pass")).AssertStatusAsync(HttpStatusCode.Created);

        Assert.Empty((await s.Api.GetJsonAsync(s.FlagsUrl)).GetProperty("items").EnumerateArray());
        s.Db.ChangeTracker.Clear();
        var flag = await s.Db.ReviewFlags.AsNoTracking().SingleAsync(f => f.AssessmentId == current.Id);
        Assert.NotNull(flag.ClearedAt);
        Assert.Equal(s.Consultant.Id, flag.ClearedById);

        // A flag can also be cleared alone, and clearing twice is harmless.
        var (_, next) = await DecidedThenRerunAsync(s);
        var open = Assert.Single((await s.Api.GetJsonAsync(s.FlagsUrl)).GetProperty("items").EnumerateArray());
        var url = $"{s.FlagsUrl}/{open.GetProperty("id").GetGuid()}/clear";
        await (await s.Api.PostAsync(url, null)).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await s.Api.PostAsync(url, null)).AssertStatusAsync(HttpStatusCode.NoContent);
        Assert.Empty((await s.Api.GetJsonAsync(s.FlagsUrl)).GetProperty("items").EnumerateArray());
        Assert.Equal(0, (await s.Api.GetJsonAsync($"{s.AssessmentsUrl}/{next.Id}")).GetProperty("decisions").GetArrayLength());
    }

    [Fact]
    [Requirement("RV-13", "After a re-run, results show a control's latest consultant verdict, marked under review, until a consultant decides on the new assessment.")]
    public async Task Results_ShowLatestVerdictUnderReview()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var notApplicable = await s.Seed.ControlAsync();
        await s.Seed.EngagementControlAsync(s.Engagement, notApplicable, applicable: false);

        JsonElement Item(JsonElement results, Guid controlId) =>
            results.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("controlId").GetGuid() == controlId);

        var control = s.Controls[0];
        var before = await s.Api.GetJsonAsync(s.ResultsUrl);
        Assert.Equal("pending", Item(before, control.Id).GetProperty("status").GetString());
        Assert.Equal("not_applicable", Item(before, notApplicable.Id).GetProperty("status").GetString());

        var (_, current) = await DecidedThenRerunAsync(s);
        var underReview = await s.Api.GetJsonAsync(s.ResultsUrl);
        Assert.Equal("pass", Item(underReview, control.Id).GetProperty("status").GetString());
        Assert.True(Item(underReview, control.Id).GetProperty("underReview").GetBoolean());
        Assert.Equal(1, underReview.GetProperty("totals").GetProperty("underReview").GetInt32());
        Assert.Equal(2, underReview.GetProperty("totals").GetProperty("total").GetInt32());

        // Clearing the flag does not end the review; only a decision on the new assessment does.
        var flag = Assert.Single((await s.Api.GetJsonAsync(s.FlagsUrl)).GetProperty("items").EnumerateArray());
        await s.Api.PostAsync($"{s.FlagsUrl}/{flag.GetProperty("id").GetGuid()}/clear", null);
        var stillUnder = await s.Api.GetJsonAsync(s.ResultsUrl);
        Assert.True(Item(stillUnder, control.Id).GetProperty("underReview").GetBoolean());

        await (await DecideAsync(s, current.Id, "fail", "Now fails.")).AssertStatusAsync(HttpStatusCode.Created);
        var decided = await s.Api.GetJsonAsync(s.ResultsUrl);
        Assert.Equal("fail", Item(decided, control.Id).GetProperty("status").GetString());
        Assert.False(Item(decided, control.Id).GetProperty("underReview").GetBoolean());
        Assert.Equal(1, decided.GetProperty("totals").GetProperty("fail").GetInt32());
    }

    // Extra coverage for AC-5 and the client rule in CLAUDE.md. It adds no requirement of its own.
    [Fact]
    public async Task ClientUser_SeesOnlyResultsAndNothingPreliminary()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var assessment = await AssessOnceAsync(s);
        await (await DecideAsync(s, assessment.Id, "fail", "Secret consultant note.")).AssertStatusAsync(HttpStatusCode.Created);
        var clientApi = s.Factory.CreateClientFor(await s.Seed.ClientUserAsync(s.Client));

        var results = await clientApi.GetAsync(s.ResultsUrl);
        await results.AssertStatusAsync(HttpStatusCode.OK);
        var body = await results.Content.ReadAsStringAsync();
        Assert.Contains("\"fail\"", body);
        foreach (var hidden in new[] { "Secret consultant note", "rationale", "citations", "preliminary", "notes", "The evidence supports" })
        {
            Assert.DoesNotContain(hidden, body, StringComparison.OrdinalIgnoreCase);
        }

        await (await clientApi.GetAsync(s.AssessmentsUrl)).AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        await (await clientApi.GetAsync($"{s.AssessmentsUrl}/{assessment.Id}")).AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        await (await clientApi.PostAsJsonAsync($"{s.AssessmentsUrl}/{assessment.Id}/decisions", new { finalVerdict = "pass" }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        await (await clientApi.GetAsync(s.FlagsUrl)).AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        await (await clientApi.GetAsync(s.ExportUrl)).AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    // Extra coverage for AC-6 on the review routes. AC-6 itself stays pending in AccessTests.
    [Fact]
    public async Task ReviewRoutes_NeverReachAnotherEngagement()
    {
        await using var s = await RunScenario.CreateAsync(postgres, controls: 1);
        var other = await s.Seed.EngagementAsync(s.Consultant, s.Client, EngagementStatus.Active);
        var control = await s.Seed.ControlAsync();
        var run = await s.Seed.RunAsync(other, s.Consultant, RunStatus.Completed);
        var foreign = await s.Seed.AssessmentAsync(run, control, s.Consultant);
        var older = await s.Seed.AssessmentAsync(await s.Seed.RunAsync(other, s.Consultant, RunStatus.Completed), await s.Seed.ControlAsync(), s.Consultant);
        var flag = await s.Seed.FlagAsync(foreign, older);

        await (await s.Api.GetAsync($"{s.AssessmentsUrl}/{foreign.Id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        await (await DecideAsync(s, foreign.Id, "pass")).AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        await (await s.Api.PostAsync($"{s.FlagsUrl}/{flag.Id}/clear", null)).AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        await (await s.Api.GetAsync($"/api/engagements/{s.Engagement.Id}/controls/{control.Id}/assessments")).AssertProblemAsync(HttpStatusCode.NotFound, "not-found");

        Assert.Equal(0, await DecisionCountAsync(s));
        s.Db.ChangeTracker.Clear();
        Assert.Null((await s.Db.ReviewFlags.AsNoTracking().SingleAsync(f => f.Id == flag.Id)).ClearedAt);
    }
}
