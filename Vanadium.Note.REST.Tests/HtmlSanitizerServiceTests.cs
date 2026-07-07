using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Pins the server-side HTML sanitizing that guards Create/Update (issue #103):
/// active content is stripped while the Tiptap editor's markup — every custom
/// node and its data-* attributes — survives round-tripping.
/// </summary>
public class HtmlSanitizerServiceTests
{
    private readonly HtmlSanitizerService _sanitizer = new();

    [Fact]
    public void Sanitize_RemovesScriptTag()
    {
        var result = _sanitizer.Sanitize("<p>hi</p><script>steal(localStorage.jwt)</script>");

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("steal", result);
        Assert.Contains("hi", result);
    }

    [Fact]
    public void Sanitize_RemovesEventHandlerAttributes()
    {
        var result = _sanitizer.Sanitize("<img src=\"/api/files/x\" onerror=\"steal()\">");

        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("steal", result);
    }

    [Fact]
    public void Sanitize_RemovesJavascriptScheme()
    {
        var result = _sanitizer.Sanitize("<a href=\"javascript:steal()\">x</a>");

        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_PreservesDataAttributesOnCustomNodes()
    {
        const string html =
            "<div data-type=\"page-link\" data-note-id=\"abc\" data-title=\"Spec\" class=\"page-link-block\">" +
            "<span class=\"page-link-icon\">📄</span><span class=\"page-link-title\">Spec</span></div>";

        var result = _sanitizer.Sanitize(html);

        Assert.Contains("data-type=\"page-link\"", result);
        Assert.Contains("data-note-id=\"abc\"", result);
        Assert.Contains("data-title=\"Spec\"", result);
    }

    [Fact]
    public void Sanitize_PreservesToggleAndCollapsibleHeadingMarkup()
    {
        const string html =
            "<h2 data-collapsible=\"true\" data-open=\"false\">Section</h2>" +
            "<div data-type=\"toggle\" data-open=\"false\" class=\"toggle-block\">" +
            "<div data-type=\"toggle-summary\" class=\"toggle-summary\">Summary</div>" +
            "<div data-type=\"toggle-content\" class=\"toggle-content\"><p>body</p></div></div>";

        var result = _sanitizer.Sanitize(html);

        Assert.Contains("data-collapsible=\"true\"", result);
        Assert.Contains("data-type=\"toggle\"", result);
        Assert.Contains("data-type=\"toggle-summary\"", result);
        Assert.Contains("data-type=\"toggle-content\"", result);
        Assert.Contains("body", result);
    }

    [Fact]
    public void Sanitize_PreservesFileAttachmentDownloadAnchor()
    {
        const string html =
            "<a class=\"file-attachment\" data-filename=\"log.txt\" download=\"log.txt\" " +
            "href=\"/api/files/abc\">📎 log.txt</a>";

        var result = _sanitizer.Sanitize(html);

        Assert.Contains("download=\"log.txt\"", result);
        Assert.Contains("data-filename=\"log.txt\"", result);
        Assert.Contains("/api/files/abc", result);
    }

    [Fact]
    public void Sanitize_KeepsRelativeImageSource()
    {
        var result = _sanitizer.Sanitize("<img src=\"/api/files/abc\" alt=\"pic\">");

        Assert.Contains("/api/files/abc", result);
    }

    [Fact]
    public async Task Create_StripsScriptButKeepsCustomNode()
    {
        using var h = new TestHost();

        const string html =
            "<div data-type=\"callout\" data-emoji=\"💡\" class=\"callout-block\">" +
            "<div class=\"callout-content\"><p>note</p></div></div>" +
            "<script>steal(localStorage.jwt)</script>";

        var note = await h.CreateNoteAsync("Note", content: html);
        var saved = await h.FindAsync(note.Id);

        Assert.DoesNotContain("<script", saved!.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("steal", saved.Content);
        Assert.Contains("data-type=\"callout\"", saved.Content);
        Assert.Contains("data-emoji", saved.Content);
    }

    [Fact]
    public async Task Update_StripsScriptOnStore()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Note", content: "<p>clean</p>");

        note.Content = "<p>edited</p><img src=\"x\" onerror=\"steal()\">";
        var (updated, conflict, archived) = await h.Notes.Update(note.Id, note);

        Assert.NotNull(updated);
        Assert.False(conflict);
        Assert.False(archived);

        var saved = await h.FindAsync(note.Id);
        Assert.DoesNotContain("onerror", saved!.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("steal", saved.Content);
        Assert.Contains("edited", saved.Content);
    }
}
