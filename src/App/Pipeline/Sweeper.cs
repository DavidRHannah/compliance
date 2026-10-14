using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

/// <summary>
/// Fails runs that have made no progress for 30 minutes, so a consultant can resume them. Progress is an
/// assessment saved or a control failed. The sweep for unconfirmed uploads comes with documents (PR-8).
/// </summary>
public sealed class Sweeper(IServiceScopeFactory scopes, IOptions<WorkerSettings> settings, ILogger<Sweeper> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.Value.SweepIntervalMinutes));
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var failed = await FailStalledRunsAsync(db, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(settings.Value.StalledRunMinutes), stoppingToken);
                if (failed > 0)
                {
                    logger.LogWarning("The sweeper failed {Count} stalled runs", failed);
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError("The sweep failed: {ErrorType}", e.GetType().Name);
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Returns how many runs were failed.</summary>
    public static async Task<int> FailStalledRunsAsync(AppDbContext db, DateTimeOffset now, TimeSpan stall, CancellationToken ct)
    {
        var cutoff = now - stall;
        return await db.AssessmentRuns
            .Where(r => (r.Status == RunStatus.Queued || r.Status == RunStatus.Running)
                && r.CreatedAt < cutoff
                && (r.StartedAt == null || r.StartedAt < cutoff)
                && !db.Assessments.Any(a => a.RunId == r.Id && a.CreatedAt >= cutoff)
                && !db.AssessmentRunControls.Any(c => c.RunId == r.Id && c.FailedAt >= cutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.CompletedAt, now), ct);
    }
}
