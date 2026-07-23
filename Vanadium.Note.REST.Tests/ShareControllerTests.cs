using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Vanadium.Note.REST.Controllers;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Status-code and response-header contract for the anonymous share endpoint (issue #94).
/// The auth middleware ([AllowAnonymous]) and rate limiter are pipeline concerns verified
/// out-of-band; these tests pin the controller's own mapping: valid token → 200 + lean DTO,
/// unknown/revoked token → 404, and the per-mode X-Robots-Tag header.
/// </summary>
public class ShareControllerTests
{
    // A DefaultHttpContext gives the controller a real Response.Headers to write to.
    private static ShareController CreateController(TestHost h) =>
        new(h.Notes, NullLogger<ShareController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    [Fact]
    public async Task Get_ValidToken_ReturnsSharedNote()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Shared", content: "<p>body</p>");
        var info = await h.Notes.SetShare(note.Id, ShareMode.Public);
        var controller = CreateController(h);

        var result = await controller.Get(info!.Token!, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var shared = Assert.IsType<SharedNote>(ok.Value);
        Assert.Equal(note.Id, shared.Id);
        Assert.Equal("Shared", shared.Title);
    }

    [Fact]
    public async Task Get_NoteWithReferences_RedactsIdsAndTitles()
    {
        using var h = new TestHost();
        const string secretId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        const string secretTitle = "Private Referenced Note";
        var content =
            $"<p>intro</p>" +
            $"<div data-type=\"page-link\" data-note-id=\"{secretId}\" data-title=\"{secretTitle}\" class=\"page-link-block\">" +
            $"<span class=\"page-link-icon\">📄</span><span class=\"page-link-title\">{secretTitle}</span></div>" +
            $"<p>see <a data-type=\"note-mention\" data-note-id=\"{secretId}\" data-title=\"{secretTitle}\" class=\"note-mention\">@{secretTitle}</a></p>";
        var note = await h.CreateNoteAsync("Shared", content: content);
        var info = await h.Notes.SetShare(note.Id, ShareMode.Public);
        var controller = CreateController(h);

        var result = await controller.Get(info!.Token!, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var shared = Assert.IsType<SharedNote>(ok.Value);
        Assert.DoesNotContain(secretId, shared.Content);
        Assert.DoesNotContain(secretTitle, shared.Content);
        Assert.DoesNotContain("data-note-id", shared.Content);
        Assert.DoesNotContain("data-title", shared.Content);
        Assert.Contains("🔒 private page", shared.Content);
        Assert.Contains("<p>intro</p>", shared.Content); // ordinary content survives
    }

    [Fact]
    public async Task Get_UnknownToken_ReturnsNotFound()
    {
        using var h = new TestHost();
        var controller = CreateController(h);

        var result = await controller.Get("deadbeefdeadbeefdeadbeefdeadbeef", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Get_RevokedToken_ReturnsNotFound()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Shared");
        var info = await h.Notes.SetShare(note.Id, ShareMode.Public);
        await h.Notes.Unshare(note.Id);
        var controller = CreateController(h);

        var result = await controller.Get(info!.Token!, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Get_LinkMode_SetsNoIndexHeader()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Shared");
        var info = await h.Notes.SetShare(note.Id, ShareMode.Link);
        var controller = CreateController(h);

        await controller.Get(info!.Token!, CancellationToken.None);

        Assert.Equal("noindex, nofollow", controller.Response.Headers["X-Robots-Tag"].ToString());
    }

    [Fact]
    public async Task Get_PublicMode_DoesNotSetNoIndexHeader()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Shared");
        var info = await h.Notes.SetShare(note.Id, ShareMode.Public);
        var controller = CreateController(h);

        await controller.Get(info!.Token!, CancellationToken.None);

        Assert.False(controller.Response.Headers.ContainsKey("X-Robots-Tag"));
    }
}
