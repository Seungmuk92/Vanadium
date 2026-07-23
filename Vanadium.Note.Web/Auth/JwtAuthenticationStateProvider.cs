using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace Vanadium.Note.Web.Auth;

public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly TokenStore tokenStore;
    private readonly ILogger<JwtAuthenticationStateProvider> logger;

    public JwtAuthenticationStateProvider(
        TokenStore tokenStore,
        ILogger<JwtAuthenticationStateProvider> logger)
    {
        this.tokenStore = tokenStore;
        this.logger = logger;
        // Cross-tab logout/login: when another tab changes the token, TokenStore
        // invalidates its cache and raises this event so we re-publish auth state
        // (issue #134). Both services live for the app's lifetime, so the
        // subscription needs no explicit teardown.
        this.tokenStore.TokenChangedExternally += NotifyAuthStateChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
            return Anonymous;

        var claims = JwtClaimParser.ParseClaims(token);
        if (claims.Count == 0)
        {
            logger.LogWarning("JWT produced no claims — token may be malformed or corrupted.");
            return Anonymous;
        }

        // An expired token must not present as authenticated, otherwise a stale JWT flashes a
        // logged-in shell before the first API 401 forces re-login (issue #297).
        if (JwtClaimParser.IsExpired(claims, DateTimeOffset.UtcNow))
        {
            logger.LogInformation("JWT is expired — treating as anonymous.");
            return Anonymous;
        }

        var identity = new ClaimsIdentity(claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void NotifyAuthStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
