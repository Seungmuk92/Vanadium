using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;
using Vanadium.Note.Web.Auth;

namespace Vanadium.Note.Web.Services;

public class AuthTokenHandler(
    TokenStore tokenStore,
    JwtAuthenticationStateProvider authProvider,
    NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(token))
        {
            await tokenStore.ClearAsync();
            authProvider.NotifyAuthStateChanged();
            navigation.NavigateTo(BuildLoginUrl());
        }

        return response;
    }

    /// <summary>
    /// Builds the login redirect target, carrying the current location as a
    /// <c>returnUrl</c> so re-login returns the user to where the session
    /// expired (issue #117). Avoids a redirect loop when already on /login.
    /// </summary>
    private string BuildLoginUrl()
    {
        var relative = navigation.ToBaseRelativePath(navigation.Uri);
        var path = relative.Split('?', '#')[0];
        if (string.IsNullOrEmpty(path) || path.Equals("login", StringComparison.OrdinalIgnoreCase))
            return "login";
        return $"login?returnUrl={Uri.EscapeDataString("/" + relative)}";
    }
}
