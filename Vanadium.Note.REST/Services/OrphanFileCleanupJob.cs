namespace Vanadium.Note.REST.Services;

/// <summary>
/// Background service that runs a full orphan-file scan every 24 hours.
/// Acts as a safety net for any files missed by the on-delete cleanup
/// (e.g. direct DB operations, past bugs, or edge cases).
/// </summary>
public class OrphanFileCleanupJob(IServiceScopeFactory scopeFactory, ILogger<OrphanFileCleanupJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait a bit after startup before the first run
        await Task.Delay(TimeSpan.FromMinutes(1), ct);

        while (!ct.IsCancellationRequested)
        {
            await RunAsync(ct);
            await Task.Delay(Interval, ct);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("Orphan file cleanup job started.");
        try
        {
            // FileCleanupService is Scoped, so create a scope for each run
            await using var scope = scopeFactory.CreateAsyncScope();
            var cleanupService = scope.ServiceProvider.GetRequiredService<FileCleanupService>();
            await cleanupService.DeleteAllOrphansAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — do not log as error
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orphan file cleanup job encountered an error.");
        }
    }
}
