using App.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace App.Tests.Infrastructure;

/// <summary>
/// One PostgreSQL container with pgvector for the whole test run, migrated once.
/// Tests do not reset data: each test creates its own engagement, so tests stay apart
/// the same way engagements do.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Keep in step with compose.yaml and the Aurora engine version.
    public const string Image = "pgvector/pgvector:0.8.1-pg17";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public string ConnectionString => _container.GetConnectionString();

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(options, ConnectionString);
        return new AppDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
