using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

/// <summary>
/// Fails runs that have made no progress for 30 minutes, so a consultant can resume them. Progress is an
/// assessment saved or a control failed. The sweep for unconfirmed uploads comes with documents (PR-8).
/// </summary>
public sealed class Sweeper(IServiceScopeFactory scopes, IOptions<WorkerSettings> settings, ILogger<Sweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The timer ticks every 5 minutes, but the sweep can take longer than that.
        // The next tick is ignored until the current sweep finishes.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.Value.SweepIntervalMinutes));
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope(); // create a scope for the DbContext, so it is disposed after each sweep
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // get a DbContext from the scope
                // fail runs that have made no progress for 30 minutes, so someone can resume them
                
                var failed = await FailStalledRunsAsync(db, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(settings.Value.StalledRunMinutes), stoppingToken);
                if (failed > 0)
                {
                    logger.LogWarning("The sweeper failed {Count} stalled runs", failed);
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested) // ignore exceptions when the service is stopping
            {
                logger.LogError("The sweep failed: {ErrorType}", e.GetType().Name);
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct); // returns false when the service is stopping
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Returns how many runs were failed.</summary>
    public static async Task<int> FailStalledRunsAsync(AppDbContext db, DateTimeOffset now, TimeSpan stall, CancellationToken ct)
    {
        var cutoff = now - stall; // for example, now=2026-02-11 15:00:00Z, stall=30m, cutoff=2026-02-11 14:30:00Z
        return await db.AssessmentRuns
            .Where(r => (r.Status == RunStatus.Queued || r.Status == RunStatus.Running) // where the run is still active
                && r.CreatedAt < cutoff // and it was created before the cutoff
                && (r.StartedAt == null || r.StartedAt < cutoff) // and it was started before the cutoff
                && !db.Assessments.Any(a => a.RunId == r.Id && a.CreatedAt >= cutoff) // and it has no assessments created after the cutoff
                && !db.AssessmentRunControls.Any(c => c.RunId == r.Id && c.FailedAt >= cutoff)) // and it has no failed controls after the cutoff
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.CompletedAt, now), ct); // update status and completed time selected runs
    }
}
