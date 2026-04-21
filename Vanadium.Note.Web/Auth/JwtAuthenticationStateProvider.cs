using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text.Json;

namespace Vanadium.Note.Web.Auth;

public class JwtAuthenticationStateProvider(
    TokenStore tokenStore,
    ILogger<JwtAuthenticationStateProvider> logger) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
            return Anonymous;

        var claims = ParseClaimsFromJwt(token);
        var identity = new ClaimsIdentity(claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void NotifyAuthStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var payload = jwt.Split('.').ElementAtOrDefault(1);
        if (payload is null)
        {
            logger.LogWarning("JWT is malformed — missing payload segment.");
            return [];
        }

        var padded = (payload.Length % 4) switch
        {
            2 => payload + "==",
            3 => payload + "=",
            _ => payload,
        };

        try
        {
            var jsonBytes = Convert.FromBase64String(padded);
            var pairs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes);
            return pairs?.Select(p => new Claim(p.Key, p.Value.ToString())) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse JWT claims — token may be malformed or corrupted.");
            return [];
        }
    }
}
