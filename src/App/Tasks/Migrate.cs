using App.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Tasks;

/// <summary>
/// One-off task: applies pending migrations, then exits. Each migration must work
/// with the version still running.
/// </summary>
public static class Migrate
{
    public static async Task RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // EF logs each migration it applies. On a new database it first logs one failed
        // read of __EFMigrationsHistory, before it creates that table; that is expected.
        await db.Database.MigrateAsync();
    }
}
