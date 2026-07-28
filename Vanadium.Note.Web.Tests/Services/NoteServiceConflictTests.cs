using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Vanadium.Note.Web.Models;
using Vanadium.Note.Web.Services;
using Xunit;

namespace Vanadium.Note.Web.Tests.Services;

/// <summary>
/// Worst-path coverage for the "two tabs edit the same note" conflict (issue #308): a plain save
/// of a note whose server row moved on must surface as a conflict (server 409 → optimistic
/// concurrency), while an explicit force-save (<c>?force=true</c>, issue #221) overwrites and
/// succeeds. Exercises <see cref="NoteService.SaveAsync"/> against a stub transport.
/// </summary>
public sealed class NoteServiceConflictTests
{
    private sealed class ForceAwareHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("force=true"))
            {
                var ok = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new NoteItem { Title = "forced" }),
                };
                return Task.FromResult(ok);
            }
            // Second tab already advanced the row → optimistic concurrency rejection.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
        }
    }

    private static NoteService BuildService()
    {
        var client = new HttpClient(new ForceAwareHandler()) { BaseAddress = new Uri("http://localhost/") };
        return new NoteService(client, NullLogger<NoteService>.Instance);
    }

    [Fact]
    public async Task Save_WithoutForce_ReturnsConflict()
    {
        var service = BuildService();
        var note = new NoteItem { Id = Guid.NewGuid(), Title = "tab A", Content = "<p>A</p>" };

        var result = await service.SaveAsync(note);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsConflict);
    }

    [Fact]
    public async Task Save_WithForce_Overwrites()
    {
        var service = BuildService();
        var note = new NoteItem { Id = Guid.NewGuid(), Title = "tab A", Content = "<p>A</p>" };

        var result = await service.SaveAsync(note, force: true);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsConflict);
        Assert.Equal("forced", result.Value?.Title);
    }
}
