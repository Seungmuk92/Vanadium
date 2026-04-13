using Microsoft.JSInterop;

namespace Vanadium.Note.Web.Auth;

public class TokenStore(IJSRuntime js)
{
    private string? _cachedToken;

    public async Task<string?> GetAsync()
    {
        _cachedToken ??= await js.InvokeAsync<string?>("localStorage.getItem", "authToken");
        return _cachedToken;
    }

    public async Task SetAsync(string token)
    {
        _cachedToken = token;
        await js.InvokeVoidAsync("localStorage.setItem", "authToken", token);
    }

    public async Task ClearAsync()
    {
        _cachedToken = null;
        await js.InvokeVoidAsync("localStorage.removeItem", "authToken");
    }
}
