using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Vanadium.Note.REST.Tests.SmokeE2E;

/// <summary>
/// Smoke E2E for the core happy path (issue #200): log in for a JWT, then create,
/// edit/save and re-read a note — driven over real HTTP through the full application
/// pipeline (authentication, middleware, routing, controllers, EF Core). This is the
/// deterministic, dependency-light stand-in for a browser E2E: it needs no PostgreSQL
/// and no running frontend, so it stays green in CI while still exercising the whole
/// request path end-to-end.
/// </summary>
public sealed class NoteLifecycleSmokeTests(VanadiumWebApplicationFactory factory)
    : IClassFixture<VanadiumWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Login_Create_Edit_Save_HappyPath()
    {
        var client = factory.CreateClient();

        // 1. Log in with the configured owner password and obtain a JWT.
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Password = VanadiumWebApplicationFactory.OwnerPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(login?.Token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        // 2. Create a note.
        var createResponse = await client.PostAsJsonAsync(
            "/api/notes",
            new { Title = "Smoke note", Content = "<p>original</p>" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<NoteResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created!.Id);

        // 3. Edit and save. The server treats UpdatedAt as the optimistic-concurrency
        //    token, so a normal save round-trips the version the create returned.
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/notes/{created.Id}",
            new { Title = "Smoke note (edited)", Content = "<p>edited</p>", created.UpdatedAt });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // 4. Re-read and confirm the edit was persisted.
        var fetched = await client.GetFromJsonAsync<NoteResponse>($"/api/notes/{created.Id}", JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal("Smoke note (edited)", fetched!.Title);
        Assert.Contains("edited", fetched.Content);
    }

    private sealed record LoginResponse(string Token);

    private sealed record NoteResponse(Guid Id, string Title, string Content, DateTime UpdatedAt);
}
