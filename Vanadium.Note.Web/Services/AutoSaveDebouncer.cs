namespace Vanadium.Note.Web.Services;

/// <summary>
/// Debounces auto-save: each <see cref="Schedule"/> call cancels the previous
/// pending timer and starts a new one, firing the supplied save callback only
/// after <c>delayMs</c> of quiet. Extracted so the note editor and the sub-note
/// dialog share one debounce implementation (issue #124).
/// </summary>
public sealed class AutoSaveDebouncer(int delayMs = 1500) : IDisposable
{
    private CancellationTokenSource? _cts;

    /// <summary>Restarts the debounce timer; <paramref name="save"/> runs once the
    /// delay elapses without another <see cref="Schedule"/> or <see cref="Cancel"/> call.</summary>
    public void Schedule(Func<Task> save)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        _ = RunAsync(save, _cts.Token);
    }

    private async Task RunAsync(Func<Task> save, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMs, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested) return;
        await save();
    }

    /// <summary>Cancels any pending save so it never fires.</summary>
    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Cancel();
}
