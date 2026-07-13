using Microsoft.JSInterop;

namespace Vanadium.Note.Web.Auth;

public class TokenStore(IJSRuntime js, ILogger<TokenStore> logger) : IAsyncDisposable
{
    private string? _cachedToken;
    private DotNetObjectReference<TokenStore>? _objRef;

    /// <summary>
    /// Raised when another browser tab changes the stored auth token (cross-tab
    /// login/logout sync, issue #134). The local <see cref="_cachedToken"/> cache
    /// is already updated to the new value when this fires.
    /// </summary>
    public event Action? TokenChangedExternally;

    /// <summary>
    /// Registers the cross-tab <c>storage</c> listener. Idempotent — safe to call
    /// again if the hosting layout is re-created.
    /// </summary>
    public async Task InitAsync()
    {
        try
        {
            _objRef ??= DotNetObjectReference.Create(this);
            await js.InvokeVoidAsync("tokenSync.init", _objRef);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize cross-tab token sync.");
        }
    }

    public async Task<string?> GetAsync()
    {
        try
        {
            _cachedToken ??= await js.InvokeAsync<string?>("localStorage.getItem", "authToken");
            return _cachedToken;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read auth token from localStorage.");
            return null;
        }
    }

    public async Task SetAsync(string token)
    {
        try
        {
            _cachedToken = token;
            await js.InvokeVoidAsync("localStorage.setItem", "authToken", token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist auth token to localStorage.");
        }
    }

    public async Task ClearAsync()
    {
        try
        {
            _cachedToken = null;
            await js.InvokeVoidAsync("localStorage.removeItem", "authToken");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear auth token from localStorage.");
        }
    }

    /// <summary>
    /// Invoked from JS when another tab changes the <c>authToken</c> localStorage key.
    /// Invalidates the cached token so subsequent reads reflect the external change,
    /// then notifies subscribers so auth state can refresh.
    /// </summary>
    [JSInvokable]
    public void OnExternalTokenChange(string? newToken)
    {
        _cachedToken = string.IsNullOrWhiteSpace(newToken) ? null : newToken;
        TokenChangedExternally?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await js.InvokeVoidAsync("tokenSync.dispose");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dispose cross-tab token sync.");
        }
        _objRef?.Dispose();
    }
}
