using App.Common.Access;
using App.Data;
using App.Data.Entities;
using App.Features.Assessments;
using App.Features.Decisions;
using App.Features.EngagementResults;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Export;

/// <summary>
/// The engagement's current verdicts, rationales, decisions and verified citations as an XLSX file (RV-10, AS-6).
/// Every value is written as text, so a quote that starts with "=" is never a formula. Nothing in the
/// file is logged.
/// </summary>
public static class ExportResults
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/export", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var engagementId = access.EngagementId;
        var results = await ResultsQuery.LoadAsync(db, engagementId, ct);

        var applicability = await db.EngagementControls.AsNoTracking()
            .Where(ec => ec.EngagementId == engagementId)
            .ToDictionaryAsync(ec => ec.ControlId, ec => ec.Justification, ct);
        var current = await db.Assessments.AsNoTracking()
            .Where(a => a.EngagementId == engagementId && a.IsCurrent)
            .ToDictionaryAsync(a => a.ControlId, ct);
        var openFlags = await db.ReviewFlags.AsNoTracking()
            .Where(f => f.EngagementId == engagementId && f.ClearedAt == null)
            .GroupBy(f => f.AssessmentId)
            .Select(g => new { AssessmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.AssessmentId, g => g.Count, ct);

        // The latest decision for each control, on any of its assessments.
        var latest = (await DecisionQueries.OfRecord(db, engagementId).ToListAsync(ct))
            .GroupBy(d => d.ControlId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id).First());

        var citations = await AssessmentQueries.VerifiedCitationsAsync(db, engagementId, current.Values.Select(a => a.Id).ToArray(), ct);
        var byAssessment = citations.ToLookup(c => c.AssessmentId);

        using var workbook = new XLWorkbook();
        var controls = workbook.AddWorksheet("Controls");
        WriteRow(controls, 1, "Control", "Theme", "Title", "Result", "Under review", "Applicability justification",
            "Preliminary verdict", "Rationale", "Assessed at", "Decision", "Decided by", "Decided at", "Notes", "Open flags");
        var row = 2;
        foreach (var r in results)
        {
            current.TryGetValue(r.ControlId, out var assessment);
            latest.TryGetValue(r.ControlId, out var decision);
            WriteRow(controls, row++,
                r.RefCode, r.Theme, r.Title, Name(r.Status), r.UnderReview ? "yes" : "no",
                applicability.GetValueOrDefault(r.ControlId),
                assessment is null ? null : Name(assessment.Verdict), assessment?.Rationale,
                assessment?.CreatedAt.ToString("u"),
                decision is null ? null : decision.ToResponse().Accepted ? "accepted" : "overridden",
                decision?.DecidedByName, decision?.DecidedAt.ToString("u"), decision?.Notes,
                assessment is null ? null : openFlags.GetValueOrDefault(assessment.Id).ToString());
        }

        var sheet = workbook.AddWorksheet("Citations");
        WriteRow(sheet, 1, "Control", "Document", "Section", "Location", "Quote");
        row = 2;
        foreach (var r in results)
        {
            if (!current.TryGetValue(r.ControlId, out var assessment))
            {
                continue;
            }

            foreach (var c in byAssessment[assessment.Id])
            {
                WriteRow(sheet, row++, r.RefCode, c.DocumentName, c.SectionTitle, c.Locator.GetRawText(), c.QuotedText);
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Results.File(stream.ToArray(), ContentType, $"results-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private static string Name<T>(T value) where T : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private static void WriteRow(IXLWorksheet sheet, int row, params string?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            // Text only: setting a string never parses it as a formula.
            sheet.Cell(row, i + 1).SetValue(values[i] ?? "");
        }
    }
}
