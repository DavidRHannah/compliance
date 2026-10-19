using System.Text;
using App.Data;
using App.Data.Entities;
using App.Integrations.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace App.Tasks;

/// <summary>
/// One-off task: loads the control catalog from a CSV (Reference, Theme, Title, Control statement).
/// Running it again updates the text of existing controls and adds new ones. It never deletes.
/// </summary>
public static class ImportCatalog
{
    public const string FrameworkKey = "iso_27001";
    public const string FrameworkName = "ISO/IEC 27001";
    public const string FrameworkVersion = "2022";

    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ImportCatalog));

        // --file <path> for a developer machine; otherwise the CSV comes from the catalog bucket.
        var fileIndex = Array.IndexOf(args, "--file");
        using var csv = fileIndex >= 0 && fileIndex + 1 < args.Length
            ? new StreamReader(args[fileIndex + 1])
            : await scope.ServiceProvider.GetRequiredService<ICatalogSource>().OpenAsync(CancellationToken.None);

        var (added, updated) = await RunAsync(db, csv, FrameworkKey, FrameworkName, FrameworkVersion);

        // Counts only: the statements are licensed text and stay out of the logs.
        log.LogInformation("Catalog import finished: {Added} controls added, {Updated} updated.", added, updated);
    }

    public static async Task<(int Added, int Updated)> RunAsync(
        AppDbContext db, TextReader csv, string key, string name, string version)
    {
        var rows = Parse(csv);
        if (rows.Count < 2 || !rows[0].SequenceEqual(["Reference", "Theme", "Title", "Control statement"]))
        {
            throw new InvalidOperationException("The catalog CSV must start with: Reference,Theme,Title,Control statement.");
        }

        var framework = await db.Frameworks.SingleOrDefaultAsync(f => f.Key == key && f.Version == version);
        if (framework is null)
        {
            framework = new Framework { Key = key, Name = name, Version = version };
            db.Frameworks.Add(framework);
            await db.SaveChangesAsync();
        }

        var existing = await db.Controls.Where(c => c.FrameworkId == framework.Id).ToDictionaryAsync(c => c.RefCode);
        int added = 0, updated = 0;
        foreach (var row in rows.Skip(1))
        {
            if (row.Count != 4)
            {
                throw new InvalidOperationException($"Catalog row for '{(row.Count > 0 ? row[0] : "")}' does not have 4 columns.");
            }

            // The CSV says A.5.1; the database says 5.1. The themes are lower case in the database.
            var refCode = row[0].Trim().RemovePrefix("A.");
            var theme = row[1].Trim().ToLowerInvariant();
            if (existing.TryGetValue(refCode, out var control))
            {
                control.Theme = theme;
                control.Title = row[2].Trim();
                control.Statement = row[3].Trim();
                updated++;
            }
            else
            {
                db.Controls.Add(new Control
                {
                    FrameworkId = framework.Id,
                    RefCode = refCode,
                    Theme = theme,
                    Title = row[2].Trim(),
                    Statement = row[3].Trim(),
                });
                added++;
            }
        }

        await db.SaveChangesAsync();
        return (added, updated);
    }

    private static string RemovePrefix(this string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;

    /// <summary>Reads CSV with quoted fields, doubled quotes and line breaks inside quotes. Skips blank lines.</summary>
    private static List<List<string>> Parse(TextReader reader)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        int c;
        while ((c = reader.Read()) >= 0)
        {
            var ch = (char)c;
            if (quoted)
            {
                if (ch == '"' && reader.Peek() == '"')
                {
                    reader.Read();
                    field.Append('"');
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }

                EndRow();
            }
            else
            {
                field.Append(ch);
            }
        }

        EndRow();
        return rows;

        void EndRow()
        {
            if (row.Count > 0 || field.Length > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            row = [];
            field.Clear();
        }
    }
}
