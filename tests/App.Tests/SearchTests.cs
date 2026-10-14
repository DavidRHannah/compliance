using App.Data.Entities;
using App.Pipeline.Assessments;
using App.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace App.Tests;

/// <summary>Evidence search: ranking, and the plan of the vector query.</summary>
[Collection(PostgresCollection.Name)]
public class SearchTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Search_RanksTheBestMatchFirst()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var engagement = await seed.EngagementAsync(consultant);
        var document = await seed.DocumentAsync(engagement, consultant, DocumentStatus.Ready);
        var backups = await seed.ChunkAsync(document, 0, "Backups of all production databases run every night.");
        await seed.ChunkAsync(document, 1, "Visitors must sign in at reception and wear a badge.");

        var found = await EvidenceSearch.FindAsync(db, new FakeEmbedder(), engagement.Id, ["database backups every night"], CancellationToken.None);

        Assert.Equal(backups.Id, found[0]);
    }

    [Fact]
    public async Task Search_UsesAtMostTwelveChunks()
    {
        await using var db = postgres.CreateDbContext();
        var seed = new Seed(db);
        var consultant = await seed.ConsultantAsync();
        var engagement = await seed.EngagementAsync(consultant);
        var document = await seed.DocumentAsync(engagement, consultant, DocumentStatus.Ready);
        for (var i = 0; i < 15; i++)
        {
            await seed.ChunkAsync(document, i, $"Access control policy section {i} covers user access reviews.");
        }

        var found = await EvidenceSearch.FindAsync(db, new FakeEmbedder(), engagement.Id, ["access control policy"], CancellationToken.None);

        Assert.Equal(EvidenceSearch.Keep, found.Count);
        Assert.Equal(found.Count, found.Distinct().Count());
    }

    [Fact]
    public void TsQuery_JoinsWordsWithOr_AndKeepsOnlyLettersAndDigits()
    {
        Assert.Equal("information | security | policy", EvidenceSearch.TsQuery("Information security policy!"));
        Assert.Equal("drop | table", EvidenceSearch.TsQuery("'; drop table & (a) | !"));
        Assert.Equal("", EvidenceSearch.TsQuery("a & !"));
    }

    [Fact]
    public async Task VectorSearch_UsesThePartialHnswIndex()
    {
        await using var db = postgres.CreateDbContext();
        var vector = string.Join(',', FakeEmbedder.Embed("information security policy").Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        var sql = string.Format(EvidenceSearch.VectorSql, $"'{Guid.NewGuid()}'::uuid", $"'{FakeEmbedder.Model}'", $"'[{vector}]'::vector", EvidenceSearch.PerSearch);

        // The test database holds few rows, so the planner is told not to scan the table.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "EXPLAIN " + sql;
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        Assert.Contains(plan, line => line.Contains("ix_document_chunks_embedding"));
    }
}
