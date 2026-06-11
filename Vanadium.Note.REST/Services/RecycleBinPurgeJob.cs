using System.Diagnostics;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Background service that permanently deletes notes which have been in the
/// recycle bin longer than the configured retention period (RecycleBin:RetentionDays,
/// default 30 days). Runs every 6 hours; purge precision does not need to be
/// tight, so a coarse interval keeps the load negligible.
/// </summary>
public class RecycleBinPurgeJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RecycleBinPurgeJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int DefaultRetentionDays = 30;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait a bit after startup before the first run
        await Task.Delay(TimeSpan.FromMinutes(2), ct);

        while (!ct.IsCancellationRequested)
        {
            await RunAsync(ct);
            await Task.Delay(Interval, ct);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var retentionDays = configuration.GetValue("RecycleBin:RetentionDays", DefaultRetentionDays);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        logger.LogInformation(
            "Recycle Bin purge job started (retention: {RetentionDays} day(s), cutoff: {Cutoff}).",
            retentionDays, cutoff);
        var sw = Stopwatch.StartNew();
        try
        {
            // NoteService is Scoped, so create a scope for each run
            await using var scope = scopeFactory.CreateAsyncScope();
            var noteService = scope.ServiceProvider.GetRequiredService<NoteService>();
            var purged = await noteService.PurgeExpired(cutoff, ct);
            sw.Stop();
            logger.LogInformation(
                "Recycle Bin purge job completed in {ElapsedMs}ms — {PurgedCount} note(s) purged. Next run in ~{IntervalHours}h.",
                sw.ElapsedMilliseconds, purged, (int)Interval.TotalHours);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Recycle Bin purge job cancelled after {ElapsedMs}ms (application shutting down).",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Recycle Bin purge job encountered an error after {ElapsedMs}ms.",
                sw.ElapsedMilliseconds);
        }
    }
}
