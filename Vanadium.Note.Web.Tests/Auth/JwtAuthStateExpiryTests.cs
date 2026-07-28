using System.Buffers.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using Vanadium.Note.Web.Auth;
using Xunit;

namespace Vanadium.Note.Web.Tests.Auth;

/// <summary>
/// Worst-path coverage for "save while the session expires" (issue #308): an expired JWT must
/// resolve to an anonymous auth state so a stale token never flashes a logged-in shell before the
/// first API 401 forces re-login (issue #297). Drives the real <see cref="TokenStore"/> over a
/// bUnit-mocked localStorage so the whole client auth read path is exercised.
/// </summary>
public sealed class JwtAuthStateExpiryTests : TestContext
{
    private JwtAuthenticationStateProvider CreateProvider(string? token)
    {
        JSInterop.Setup<string?>("localStorage.getItem", "authToken").SetResult(token);
        var store = new TokenStore(JSInterop.JSRuntime, NullLogger<TokenStore>.Instance);
        return new JwtAuthenticationStateProvider(store, NullLogger<JwtAuthenticationStateProvider>.Instance);
    }

    private static string MakeJwt(long expUnixSeconds)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { name = "owner", exp = expUnixSeconds });
        return $"header.{Base64Url.EncodeToString(payload)}.signature";
    }

    [Fact]
    public async Task ExpiredToken_ResolvesAnonymous()
    {
        var expired = MakeJwt(DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds());
        var provider = CreateProvider(expired);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task ValidToken_ResolvesAuthenticated()
    {
        var valid = MakeJwt(DateTimeOffset.UtcNow.AddMinutes(60).ToUnixTimeSeconds());
        var provider = CreateProvider(valid);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity?.IsAuthenticated ?? false);
        // The provider maps raw JWT keys verbatim, so the owner name is carried on the "name" claim.
        Assert.Equal("owner", state.User.FindFirst("name")?.Value);
    }

    [Fact]
    public async Task NoToken_ResolvesAnonymous()
    {
        var provider = CreateProvider(null);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated ?? false);
    }
}
