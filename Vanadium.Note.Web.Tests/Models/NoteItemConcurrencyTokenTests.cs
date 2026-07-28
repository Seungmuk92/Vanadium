using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Vanadium.Note.Web.Models;
using Vanadium.Note.Web.Services;
using Xunit;

namespace Vanadium.Note.Web.Tests.Models;

/// <summary>
/// Guards the Web DTO concurrency-token default (issue #312). <see cref="NoteItem.UpdatedAt"/> is
/// the optimistic-concurrency token echoed to the server on save; its default must be an
/// obviously-empty <c>default</c>, never a plausible-looking <c>DateTime.UtcNow</c>. A "now" default
/// silently produced a version the server row could never match, so any construction that forgot to
/// copy the tracked timestamp made every save 409. These tests fail if that default ever regresses
/// back to a non-default value.
/// </summary>
public sealed class NoteItemConcurrencyTokenTests
{
    [Fact]
    public void UpdatedAt_DefaultsToDefault_NotUtcNow()
    {
        var note = new NoteItem();

        Assert.Equal(default, note.UpdatedAt);
    }

    /// <summary>
    /// Captures the JSON actually sent to the server. Without the guard, a note constructed without
    /// setting <see cref="NoteItem.UpdatedAt"/> would ship a fresh <c>DateTime.UtcNow</c> as the
    /// concurrency version — a value the server row can never match — re-introducing the spurious
    /// 409. With the guard the serialized version is <c>default</c> (the unmistakably-unset sentinel).
    /// </summary>
    [Fact]
    public async Task Save_WithoutSettingUpdatedAt_SendsDefaultVersion_NotAFreshTimestamp()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var service = new NoteService(client, NullLogger<NoteService>.Instance);

        // A caller that forgets to copy the server-tracked timestamp (the exact mistake #312 guards).
        var note = new NoteItem { Id = Guid.NewGuid(), Title = "no version set", Content = "<p>x</p>" };

        await service.SaveAsync(note);

        Assert.NotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var sentUpdatedAt = doc.RootElement.GetProperty("updatedAt").GetDateTime();
        Assert.Equal(default, sentUpdatedAt);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new NoteItem { Title = "saved" }),
            };
        }
    }
}
