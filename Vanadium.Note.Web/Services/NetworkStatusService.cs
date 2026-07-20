using Microsoft.JSInterop;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// Tracks browser connectivity — <c>navigator.onLine</c> plus the window
/// <c>online</c>/<c>offline</c> events — and raises <see cref="OnChanged"/> whenever it
/// flips, so the offline banner and the pending-save flush both react to a reconnection
/// (issue #211).
/// </summary>
/// <remarks>
/// <c>navigator.onLine</c> reports link-layer connectivity, not reachability of the API:
/// it can read <c>true</c> behind a captive portal or while the server is down. It is
/// therefore used only to CLASSIFY a failure that already happened, never to pre-empt a
/// request — a save is always attempted first and only parked once it actually fails.
/// </remarks>
public sealed class NetworkStatusService(IJSRuntime js, ILogger<NetworkStatusService> logger) : IAsyncDisposable
{
    private DotNetObjectReference<NetworkStatusService>? _objRef;

    /// <summary>
    /// Optimistic default: until <see cref="InitAsync"/> reports the real state, assume
    /// online so the banner never flashes during a healthy start-up.
    /// </summary>
    public bool IsOnline { get; private set; } = true;

    public event Action? OnChanged;

    public async Task InitAsync()
    {
        try
        {
            _objRef ??= DotNetObjectReference.Create(this);
            SetOnline(await js.InvokeAsync<bool>("networkStatus.init", _objRef));
        }
        catch (Exception ex)
        {
            // Interop failure must not take the layout down; connectivity simply stays
            // at the optimistic default and saves behave exactly as they did before.
            logger.LogError(ex, "Failed to initialize network status tracking.");
        }
    }

    [JSInvokable]
    public void SetOnline(bool online)
    {
        if (IsOnline == online) return;
        IsOnline = online;
        OnChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await js.InvokeVoidAsync("networkStatus.dispose");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dispose network status tracking.");
        }
        _objRef?.Dispose();
    }
}
