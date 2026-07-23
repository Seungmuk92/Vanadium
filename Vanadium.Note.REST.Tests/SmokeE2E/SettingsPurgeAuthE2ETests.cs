using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Vanadium.Note.REST.Tests.SmokeE2E;

/// <summary>
/// Auth-scheme lockdown for the all-data purge (issue #289), driven over real HTTP through
/// the full pipeline. A personal access token must not be able to call
/// DELETE /api/settings/all-data (JWT-only scheme → 401), and a JWT carrying the wrong
/// password must be refused (403). Both assertions are non-destructive — the purge never
/// runs — so they safely share one factory/database.
/// </summary>
public sealed class SettingsPurgeAuthE2ETests(VanadiumWebApplicationFactory factory)
    : IClassFixture<VanadiumWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Password = VanadiumWebApplicationFactory.OwnerPassword });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        return login!.Token;
    }

    [Fact]
    public async Task PurgeWithPat_IsRejectedByJwtOnlyScheme()
    {
        var client = factory.CreateClient();
        var jwt = await LoginAsync(client);

        // Mint a personal access token using the interactive JWT.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var createResponse = await client.PostAsJsonAsync(
            "/api/apitokens", new { Name = "ci-token", ExpiresInDays = (int?)null });
        createResponse.EnsureSuccessStatusCode();
        var pat = await createResponse.Content.ReadFromJsonAsync<CreateTokenResponse>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(pat?.Token));

        // Even with the correct password in the body, the PAT must not authenticate against
        // the JWT-only purge endpoint — it is rejected before any deletion happens.
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/settings/all-data")
        {
            Content = JsonContent.Create(new { Password = VanadiumWebApplicationFactory.OwnerPassword })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat!.Token);
        var purge = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, purge.StatusCode);
    }

    [Fact]
    public async Task PurgeWithJwtAndWrongPassword_IsForbidden()
    {
        var client = factory.CreateClient();
        var jwt = await LoginAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/settings/all-data")
        {
            Content = JsonContent.Create(new { Password = "definitely-not-the-password" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var purge = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, purge.StatusCode);
    }

    private sealed record LoginResponse(string Token);

    private sealed record CreateTokenResponse(string Token);
}
