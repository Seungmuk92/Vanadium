using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Direct unit coverage for the pure helpers extracted into <see cref="NoteReferenceRewriter"/>
/// (issue #311 — NoteService split). The reference-rewrite transforms are exercised end-to-end
/// through the facade in <see cref="ContentRewriteRobustnessTests"/>; these tests pin the small
/// primitives (HTML stripping, write-timestamp truncation) that are now independently reachable
/// public surface.
/// </summary>
public class NoteReferenceRewriterTests
{
    [Fact]
    public void StripHtml_ReplacesTagsAndEntities_WithCollapsedWhitespace()
    {
        var html = "<p>Hello&nbsp;  <strong>world</strong></p>";
        // Each tag and each entity becomes a space, then runs of whitespace collapse to one and
        // the result is trimmed — so the entity itself (&nbsp;) is dropped, not decoded.
        Assert.Equal("Hello world", NoteReferenceRewriter.StripHtml(html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void StripHtml_NullOrEmpty_ReturnsEmpty(string? html)
    {
        Assert.Equal(string.Empty, NoteReferenceRewriter.StripHtml(html!));
    }

    [Fact]
    public void UtcNowMicroseconds_TruncatesTo_MicrosecondPrecision()
    {
        var stamp = NoteReferenceRewriter.UtcNowMicroseconds();
        // PostgreSQL stores microseconds (6 digits); the sub-microsecond tick digit must be zeroed
        // so a round-trip cannot manufacture a false optimistic-concurrency conflict.
        Assert.Equal(0, stamp.Ticks % 10);
        Assert.Equal(DateTimeKind.Utc, stamp.Kind);
    }

    [Fact]
    public void UpdatePageLinkTitleInContent_RewritesTitle_InTextContentNotOnlyAttribute()
    {
        var id = Guid.NewGuid();
        var content = $"<div data-note-id=\"{id}\" data-title=\"Old\">📄 Old</div>";

        var updated = NoteReferenceRewriter.UpdatePageLinkTitleInContent(content, id, "New");

        Assert.Contains("📄 New", updated);
        Assert.Contains("data-title=\"New\"", updated);
    }

    [Fact]
    public void UpdatePageLinkTitleInContent_NoMatchingReference_ReturnsInputUnchanged()
    {
        var content = "<div data-note-id=\"" + Guid.NewGuid() + "\">📄 Other</div>";
        var updated = NoteReferenceRewriter.UpdatePageLinkTitleInContent(content, Guid.NewGuid(), "New");
        Assert.Equal(content, updated);
    }

    [Fact]
    public void StripMentionLinksFromContent_UnwrapsAnchor_KeepingVisibleText()
    {
        var id = Guid.NewGuid();
        var content = $"<p>Hi <a data-note-id=\"{id}\" data-title=\"Bob\">@Bob</a></p>";

        var updated = NoteReferenceRewriter.StripMentionLinksFromContent(content, id);

        Assert.DoesNotContain("<a", updated);
        Assert.Contains("@Bob", updated);
    }
}
