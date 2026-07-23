using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vanadium.Note.REST.Auth;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Security;
using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// A valid personal access token must authenticate even while the shared PAT throttle is locked;
/// otherwise an attacker's invalid attempts deny the owner's automations service (issue #291,
/// mirroring the login fix). Invalid tokens are still refused and still feed the throttle.
/// </summary>
public class ApiTokenAuthHandlerTests
{
    // Minimal IOptionsMonitor so AuthenticationHandler<T> can construct without the DI stack.
    private sealed class StubOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        private readonly AuthenticationSchemeOptions _value = new();
        public AuthenticationSchemeOptions CurrentValue => _value;
        public AuthenticationSchemeOptions Get(string? name) => _value;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private static ApiTokenThrottle LockedThrottle()
    {
        var throttle = new ApiTokenThrottle(
            Options.Create(new ApiTokenThrottleOptions { FailureThreshold = 1, BaseDelaySeconds = 300, MaxDelaySeconds = 900 }),
            TimeProvider.System,
            NullLogger<ApiTokenThrottle>.Instance);
        throttle.RegisterFailure(); // threshold = 1 → immediately locked
        Assert.True(throttle.IsLocked(out _));
        return throttle;
    }

    private static async Task<ApiTokenAuthHandler> CreateHandlerAsync(
        TestHost h, IApiTokenThrottle throttle, string authHeaderValue)
    {
        var handler = new ApiTokenAuthHandler(
            new StubOptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            h.Db,
            throttle);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = authHeaderValue;

        await handler.InitializeAsync(
            new AuthenticationScheme(ApiTokenAuthHandler.SchemeName, null, typeof(ApiTokenAuthHandler)),
            httpContext);
        return handler;
    }

    private static async Task<string> SeedValidTokenAsync(TestHost h)
    {
        var plaintext = ApiTokenService.TokenPrefix + "abcdefghijklmnopqrstuvwxyz012345";
        h.Db.ApiTokens.Add(new ApiToken
        {
            Id = Guid.NewGuid(),
            Name = "ci",
            TokenHash = ApiTokenService.HashToken(plaintext),
            TokenSuffix = plaintext[^4..],
            CreatedAt = DateTime.UtcNow,
        });
        await h.Db.SaveChangesAsync();
        return plaintext;
    }

    [Fact]
    public async Task ValidToken_WhileLocked_Authenticates()
    {
        using var h = new TestHost();
        var plaintext = await SeedValidTokenAsync(h);
        var throttle = LockedThrottle();
        var handler = await CreateHandlerAsync(h, throttle, $"Bearer {plaintext}");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.False(throttle.IsLocked(out _)); // a valid token cleared the lock
    }

    [Fact]
    public async Task UnknownToken_WhileLocked_IsRejected()
    {
        using var h = new TestHost();
        await SeedValidTokenAsync(h);
        var throttle = LockedThrottle();
        var handler = await CreateHandlerAsync(h, throttle, $"Bearer {ApiTokenService.TokenPrefix}not-a-real-token-value-000000000");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
    }
}
