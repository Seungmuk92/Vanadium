using System.Net;
using System.Net.Http.Json;

namespace Vanadium.Note.Web.Auth;

public class AuthService(
    HttpClient http,
    TokenStore tokenStore,
    JwtAuthenticationStateProvider authProvider,
    ILogger<AuthService> logger)
{
    public async Task<LoginOutcome> LoginAsync(string password)
    {
        logger.LogInformation("Login attempt.");
        try
        {
            var response = await http.PostAsJsonAsync("api/auth/login", new { password });
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Login failed: HTTP {StatusCode}.", (int)response.StatusCode);
                return response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => LoginOutcome.InvalidPassword,
                    HttpStatusCode.TooManyRequests => LoginOutcome.RateLimited,
                    _ => LoginOutcome.ServerError,
                };
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResult>();
            if (result?.Token is null)
            {
                logger.LogWarning("Login failed: response contained no token.");
                return LoginOutcome.ServerError;
            }

            await tokenStore.SetAsync(result.Token);
            authProvider.NotifyAuthStateChanged();
            logger.LogInformation("Logged in successfully.");
            return LoginOutcome.Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login request failed.");
            return LoginOutcome.NetworkError;
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
