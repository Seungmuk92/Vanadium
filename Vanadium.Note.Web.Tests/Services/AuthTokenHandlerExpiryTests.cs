using System.Net;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vanadium.Note.Web.Auth;
using Vanadium.Note.Web.Services;
using Xunit;

namespace Vanadium.Note.Web.Tests.Services;

/// <summary>
/// Worst-path coverage for "save while the session expires" (issue #308): when a request (e.g. an
/// auto-save PUT) comes back 401 because the JWT expired mid-session, <see cref="AuthTokenHandler"/>
/// must clear the stored token and redirect to <c>/login</c>, carrying the current location as a
/// <c>returnUrl</c> (issue #117) so re-login lands the user back on the note being edited.
/// </summary>
public sealed class AuthTokenHandlerExpiryTests : TestContext
{
    private sealed class StubInnerHandler(HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(code));
    }

    private HttpClient BuildClient(HttpStatusCode innerStatus, out FakeNavigationManager nav)
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // token writes/clears are void interop no-ops here
        JSInterop.Setup<string?>("localStorage.getItem", "authToken").SetResult("stale.jwt.token");

        var store = new TokenStore(JSInterop.JSRuntime, NullLogger<TokenStore>.Instance);
        var authProvider = new JwtAuthenticationStateProvider(store, NullLogger<JwtAuthenticationStateProvider>.Instance);
        nav = Services.GetRequiredService<FakeNavigationManager>();

        var handler = new AuthTokenHandler(store, authProvider, nav)
        {
            InnerHandler = new StubInnerHandler(innerStatus),
        };
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
    }

    [Fact]
    public async Task Unauthorized_RedirectsToLogin_WithReturnUrl()
    {
        var client = BuildClient(HttpStatusCode.Unauthorized, out var nav);
        nav.NavigateTo("editor/abc123"); // user is editing a note when the save fails

        await client.PutAsync("api/notes/abc123", new StringContent("{}"));

        Assert.Contains("login", nav.Uri);
        Assert.Contains("returnUrl", nav.Uri);
        Assert.Contains("editor", Uri.UnescapeDataString(nav.Uri));
    }

    [Fact]
    public async Task SuccessfulSave_DoesNotRedirect()
    {
        var client = BuildClient(HttpStatusCode.OK, out var nav);
        var before = nav.Uri;

        await client.PutAsync("api/notes/abc123", new StringContent("{}"));

        Assert.Equal(before, nav.Uri);
    }
}
