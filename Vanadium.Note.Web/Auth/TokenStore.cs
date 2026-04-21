using Microsoft.JSInterop;

namespace Vanadium.Note.Web.Auth;

public class TokenStore(IJSRuntime js, ILogger<TokenStore> logger)
{
    private string? _cachedToken;

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
}
