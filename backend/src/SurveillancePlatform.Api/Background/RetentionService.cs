using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SurveillancePlatform.Api.Data;
using SurveillancePlatform.Api.Options;
using SurveillancePlatform.Api.Storage;

namespace SurveillancePlatform.Api.Background;

/// <summary>
/// Enforces the retention policy: deletes attendance records (and their snapshots) older than
/// <see cref="RetentionOptions.Days"/>, then sweeps orphaned snapshot files.
/// Runs with tenant filters disabled because it spans every tenant.
/// </summary>
public sealed class RetentionService(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    ILogger<RetentionService> logger) : BackgroundService
{
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        await RunOnceSafeAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceSafeAsync(stoppingToken);
    }

    public async Task RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await PurgeAsync(scope.ServiceProvider, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Retention run failed; will retry on next interval.");
        }
    }

    internal async Task<int> PurgeAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var storage = services.GetRequiredService<ISnapshotStorage>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.Days);
        var deleted = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await db.AttendanceRecords.IgnoreQueryFilters()
                .Where(r => r.EntryTime < cutoff)
                .OrderBy(r => r.EntryTime)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            foreach (var record in batch)
            {
                if (record.EntrySnapshotKey is { } entryKey) await storage.DeleteAsync(entryKey, ct);
                if (record.ExitSnapshotKey is { } exitKey) await storage.DeleteAsync(exitKey, ct);
            }

            db.AttendanceRecords.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            deleted += batch.Count;
        }

        // Audit logs are retained per the compliance policy in docs/DEPLOYMENT.md and are not purged here.
        var orphansRemoved = await storage.DeleteOlderThanAsync(cutoff, ct);

        if (deleted > 0 || orphansRemoved > 0)
            logger.LogInformation("Retention removed {Records} attendance records and {Files} orphaned snapshot files.", deleted, orphansRemoved);

        return deleted;
    }
}
