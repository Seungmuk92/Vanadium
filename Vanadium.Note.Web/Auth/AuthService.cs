using System.Net.Http.Json;

namespace Vanadium.Note.Web.Auth;

public class AuthService(
    HttpClient http,
    TokenStore tokenStore,
    JwtAuthenticationStateProvider authProvider,
    ILogger<AuthService> logger)
{
    public async Task<bool> LoginAsync(string username, string password)
    {
        logger.LogInformation("Login attempt for user '{Username}'.", username);
        try
        {
            var response = await http.PostAsJsonAsync("api/auth/login", new { username, password });
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Login failed for '{Username}': HTTP {StatusCode}.",
                    username, (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResult>();
            if (result?.Token is null)
            {
                logger.LogWarning("Login failed for '{Username}': response contained no token.", username);
                return false;
            }

            await tokenStore.SetAsync(result.Token);
            authProvider.NotifyAuthStateChanged();
            logger.LogInformation("User '{Username}' logged in successfully.", username);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login request failed for user '{Username}'.", username);
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        await tokenStore.ClearAsync();
        authProvider.NotifyAuthStateChanged();
        logger.LogInformation("User logged out.");
    }

    private record LoginResult(string Token);
}
