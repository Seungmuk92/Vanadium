using System.Net.Http.Json;

namespace Vanadium.Note.Web.Auth;

public class AuthService(
    HttpClient http,
    TokenStore tokenStore,
    JwtAuthenticationStateProvider authProvider)
{
    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/auth/login", new { username, password });
            if (!response.IsSuccessStatusCode) return false;

            var result = await response.Content.ReadFromJsonAsync<LoginResult>();
            if (result?.Token is null) return false;

            await tokenStore.SetAsync(result.Token);
            authProvider.NotifyAuthStateChanged();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        await tokenStore.ClearAsync();
        authProvider.NotifyAuthStateChanged();
    }

    private record LoginResult(string Token);
}
