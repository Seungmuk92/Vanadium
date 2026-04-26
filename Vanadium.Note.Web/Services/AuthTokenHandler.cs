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
            navigation.NavigateTo("/login");
        }

        return response;
    }
}
